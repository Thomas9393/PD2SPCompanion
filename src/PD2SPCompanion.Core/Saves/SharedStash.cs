using D2SLib;
using D2SLib.IO;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// PD2's own shared stash, <c>pd2_shared.stash</c> in the game's Save folder.
/// <para>
/// This is not PlugY's <c>.sss</c>. It has no page records at all: after a fixed header comes
/// a single flat <see cref="ItemList"/>, and <b>each item names its own page in the
/// <see cref="Item.Location"/> field</b> - the four-bit body-slot field, which means nothing
/// for a stored item, so PD2 reused it. Page 1 reads as <c>Head</c>, page 2 as <c>Neck</c>,
/// and so on up the equipment slots. The field ordinarily called the page
/// (<see cref="Item.Page"/>, three bits) is a constant
/// <see cref="InventoryLayout.ExpansionStashPage"/>, and <c>X,Y</c> is the cell.
/// </para>
/// <para>
/// Measured in game, and it was worth measuring: every guess was wrong. Sixteen
/// items were put in a character's inventory, nine of them were moved one-per-page into
/// the shared stash, and the file came back with <c>Location</c> running 1-9 in exactly the
/// order the pages were filled, every item at <c>Page 6</c> and <c>X,Y = 0,0</c>. Neither the
/// three-bit page field nor the four-bit Y could have held ten pages, which is what gave the
/// guesses away before the test.
/// </para>
/// <para>
/// The header is kept <b>verbatim</b> apart from what is ours to set: the size at <c>0x08</c>
/// and the checksum at <c>0x0C</c>, both exactly as in a <c>.d2s</c> and both done by
/// <see cref="Header.Fix"/>, and the materials tab's counts. <b>It is not static
/// configuration</b>, as this once said - those tests only ever moved items. It holds the
/// account's statistics (<see cref="PlayerStats"/>, from <c>0x10</c>), the materials tab
/// (<see cref="Materials"/>, from <c>0x52</c>) and the shared gold (<c>0x12A</c>), all measured
/// in game.
/// </para>
/// <para>
/// <b>Season 14 writes version 3</b> (<see cref="Layout"/>, read from the launch's files): five more material counts after slot 72, the gold at
/// <c>0x134</c>, the Maps tab's counts from <c>0x138</c>, a block marked <c>gh</c> at <c>0x1DA</c>, and the items
/// at <c>0x35C</c>. Everything this class does not set is kept verbatim, as in version 2.
/// </para>
/// </summary>
public sealed class SharedStash
{
    /// <summary>
    /// Where a version keeps what this class reads: the item list, the gold and each material count.
    /// <para>
    /// The item list's place is fixed by the version rather than searched for: a header byte pair could
    /// read as "JM" by chance, and a wrong guess here silently reinterprets the whole file.
    /// </para>
    /// </summary>
    /// <param name="Version">The u32 at <c>0x04</c>.</param>
    /// <param name="ItemsAt">Where the item list starts - the header's length.</param>
    /// <param name="GoldAt">The shared gold, a u32.</param>
    /// <param name="MaterialAt">Where a material slot's u16 count sits (the editor's slots, <see cref="Materials.CodeAt"/>), or null for one the version does not keep.</param>
    /// <param name="MostGold">
    /// The most gold the game keeps in it - PD2's, not the 32-bit field's: 10,000,000 in Season 13,
    /// 25,000,000 in 14 ("Increased shared gold stash cap to 25 million", the S14 patch notes).
    /// </param>
    /// <param name="HandsOutPlain">
    /// Whether the tab hands out a rune or gem as the plain item (<c>r04</c>) rather than the stack (<c>r04s</c>):
    /// Season 14's, where runes and gems stack themselves (<see cref="Materials.Plain"/>).
    /// </param>
    /// <param name="DiscoveryAt">
    /// Where the game's Discovery Progress starts - the bits after the <c>gh</c> mark, <see cref="DiscoveryBytes"/> long -
    /// or null for a version that keeps none: Season 14's, from <c>0x1DC</c>.
    /// </param>
    public sealed record Layout(int Version, int ItemsAt, int GoldAt, Func<int, int?> MaterialAt, bool HandsOutPlain = false,
                                int MostGold = 10_000_000, int? DiscoveryAt = null)
    {
        /// <summary>Season 13 and before: the 98 slots one after another from <c>0x52</c>, the gold after them.</summary>
        public static readonly Layout Version2 = new(2, 0x12E, 0x12A, slot => slot <= Materials.Count ? MaterialsAt + 2 * (slot - 1) : null);

        /// <summary>
        /// Season 14. Slots 1-72 where they were; five counts S13 did not have follow slot 72 (the uber maps the
        /// Ubers tab gained), so slots 73-98 sit ten bytes later. Proven by a numbered file (slot n = n)
        /// read in game, every cell of the Misc, Ubers and Maps tabs - the Rejuvenation Potion, 73 here, beside the
        /// Full one. What Season 14 keeps beyond the 98 (<see cref="Materials.Season14Codes"/>, slots 99 on): the five
        /// new uber cells at the file's 73-77, the ears, Token, Mirror and Vial at its 104-113, and in the second
        /// block (<see cref="SecondBlockAt"/>) the Maps tab's 29 and the Gems tab's lower 21 at its 61-81.
        /// </summary>
        public static readonly Layout Version3 = new(3, 0x35C, 0x134, slot => slot switch
        {
            <= 72 => MaterialsAt + 2 * (slot - 1),
            <= 98 => MaterialsAt + 2 * (slot - 1) + 10,
            <= 103 => MaterialsAt + 2 * (slot - 99 + 72),
            <= 113 => MaterialsAt + 2 * (slot - 1),
            <= 142 => SecondBlockAt + 2 * (slot - 114),
            <= 163 => SecondBlockAt + 2 * (slot - 143 + 60),
            _ => null,
        }, HandsOutPlain: true, MostGold: 25_000_000, DiscoveryAt: 0x1DC);

        /// <summary>The layouts this class can read.</summary>
        public static readonly IReadOnlyList<Layout> Known = [Version2, Version3];
    }

    /// <summary>The layout of the file read.</summary>
    public Layout Shape { get; }

    /// <summary>Where this file's item list starts.</summary>
    public int HeaderLength => Shape.ItemsAt;

    /// <summary>
    /// The lowest page number, and the value <see cref="Item.Location"/> carries for it.
    /// <para>
    /// Zero is <c>ItemLocation.None</c>, which no page uses - so a page is one-based, and an
    /// item left at <c>None</c> belongs to no page at all.
    /// </para>
    /// </summary>
    public const int FirstPage = 1;

    /// <summary>
    /// How many shared pages there are: <b>nine</b>.
    /// <para>
    /// Confirmed twice over. The file came back using pages 1-9 and no more,
    /// and the stash UI shows tabs <c>P I II III IV V VI VII VIII IX</c> - a personal tab
    /// and nine numbered ones. The personal tab is <b>not in this file</b>: it is the
    /// character's own stash, kept in the <c>.d2s</c> at
    /// <see cref="InventoryLayout.ExpansionStashPage"/>.
    /// </para>
    /// <para>
    /// So the whole shared stash holds nine pages of
    /// <see cref="InventoryLayout.Stash"/> - 1350 cells, which is why a library of any size
    /// has to be carried across in batches.
    /// </para>
    /// </summary>
    public const int Pages = 9;

    /// <summary>Where material slot 1's count sits in the header; slot n's is <c>MaterialsAt + 2(n - 1)</c>.</summary>
    private const int MaterialsAt = 0x52;

    /// <summary>Version 3's second run of counts, 81 u16: the Maps tab's in its first 29, the lower gems' in its last 21.</summary>
    private const int SecondBlockAt = 0x138;

    /// <summary>The last page, for a caller walking them.</summary>
    public const int LastPage = FirstPage + Pages - 1;

    private readonly byte[] _header;

    private SharedStash(Layout shape, byte[] header, ItemList items, int statIdBits, bool lastSeason)
    {
        Shape = shape;
        _header = header;
        Items = items;
        StatIdBits = statIdBits;
        LastSeason = lastSeason;
    }

    /// <summary>Whether this file's tab keeps a slot at all: Season 13's the first 98, 14's every one.</summary>
    public bool Holds(int slot) => slot >= 1 && slot <= Materials.Every && Shape.MaterialAt(slot) is not null;

    /// <summary>How many of a material the tab holds - none for a slot this file does not keep. Slots count from one.</summary>
    public int MaterialCount(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, Materials.Every);
        return Shape.MaterialAt(slot) is { } at
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(at))
            : 0;
    }

    /// <summary>Sets how many of a material the tab holds, within what its field can. Refused for a slot this file does not keep.</summary>
    public void SetMaterialCount(int slot, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, Materials.Every);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Materials.FieldMaximum);
        int at = Shape.MaterialAt(slot)
                 ?? throw new InvalidOperationException(L($"This stash's materials tab keeps no {Materials.NameOf(slot)}."));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(_header.AsSpan(at), (ushort)count);
    }

    /// <summary>The slot an item goes into in this file's tab, and how many it counts for; null where this tab keeps none of it.</summary>
    public (int Slot, int Quantity)? Takes(Item item) =>
        Materials.TakesAny(item) is { } taken && Holds(taken.Slot) ? taken : null;

    /// <summary>The code a material comes out of the tab as: the stack in Season 13's file, the plain item in 14's.</summary>
    public string MaterialCode(int slot) => Shape.HandsOutPlain ? Materials.Plain(slot) : Materials.CodeAt(slot);

    /// <summary>The shared gold, as the tab's gold box shows it.</summary>
    public uint Gold => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(Shape.GoldAt));

    /// <summary>
    /// The most any shared stash holds, whatever its season: the largest <see cref="Layout.MostGold"/> - what an amount
    /// box may take before the file it lands in says its own.
    /// </summary>
    public const int GoldCap = 25_000_000;

    /// <summary>The most this file's shared stash holds: its season's (<see cref="Layout.MostGold"/>).</summary>
    public int MostGold => Shape.MostGold;

    /// <summary>Sets the shared gold, within <see cref="MostGold"/>.</summary>
    public void SetGold(int gold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gold, MostGold);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(Shape.GoldAt), (uint)gold);
    }

    /// <summary>How long the Discovery block is: 384 bytes, from its start to the item list's.</summary>
    public const int DiscoveryBytes = 384;

    /// <summary>Whether this file keeps the game's Discovery Progress: Season 14's does.</summary>
    public bool KeepsDiscovery => Shape.DiscoveryAt is not null;

    /// <summary>
    /// Where in the block the set pieces begin: bit 2048 (byte 256), then one bit a row of <c>setitems.txt</c>. Proven by
    /// two set finds (the game's "1% (2/128)"): Immortal King's Soul Cage (row 71) at bit 2119 and
    /// Immortal King's Stone Crusher (row 75) at bit 2123. The uniques' rows run below it (483 the last in S14).
    /// </summary>
    public const int SetsFrom = 2048;

    /// <summary>What the game has discovered: the uniques by their <c>uniqueitems.txt</c> row, the set pieces by their <c>setitems.txt</c> row.</summary>
    public sealed record Discovery(IReadOnlySet<int> Uniques, IReadOnlySet<int> Sets)
    {
        public static Discovery None => new(new HashSet<int>(), new HashSet<int>());
    }

    /// <summary>
    /// What the game has discovered - read only: the record is the game's, and the editor never writes it.
    /// One bit a row, low bit first: bit n is byte <c>n / 8</c> of the block, bit <c>n % 8</c>; a unique's
    /// bit its row (proven by two finds, each the only change in the file: Coif of Glory, row 73, and Flamebellow, row
    /// 353), a set piece's <see cref="SetsFrom"/> plus its row. None for a file
    /// that keeps none.
    /// </summary>
    public Discovery Discovered => DiscoveryIn(Shape, _header);

    public IReadOnlySet<int> DiscoveredUniques => Discovered.Uniques;

    /// <summary>
    /// The same from a file on disk, its header alone - no item is read, so no table is needed: what the Grail page asks
    /// each time it shows, since the game writes the file as it plays. None for a file that is missing, unknown or keeps
    /// no Discovery.
    /// </summary>
    public static Discovery DiscoveredIn(string path)
    {
        try
        {
            if (!File.Exists(path))
                return Discovery.None;
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8)
                return Discovery.None;
            int version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            return Layout.Known.FirstOrDefault(each => each.Version == version) is { } shape && bytes.Length >= shape.ItemsAt
                ? DiscoveryIn(shape, bytes)
                : Discovery.None;
        }
        catch (IOException)
        {
            // The game writing it this moment: none this time, the next read has them.
            return Discovery.None;
        }
    }

    public static IReadOnlySet<int> DiscoveredUniquesIn(string path) => DiscoveredIn(path).Uniques;

    private static Discovery DiscoveryIn(Layout shape, byte[] header)
    {
        var uniques = new HashSet<int>();
        var sets = new HashSet<int>();
        if (shape.DiscoveryAt is not { } at)
            return new Discovery(uniques, sets);
        for (int bit = 0; bit < DiscoveryBytes * 8; bit++)
        {
            if ((header[at + bit / 8] >> (bit % 8) & 1) == 0)
                continue;
            if (bit < SetsFrom)
                uniques.Add(bit);
            else
                sets.Add(bit - SetsFrom);
        }
        return new Discovery(uniques, sets);
    }

    /// <summary>The account-wide statistics the header keeps, in the same layout as a character's.</summary>
    public PlayerStats Stats => PlayerStats.Read(_header.AsSpan(0x10, PlayerStats.Length));

    /// <summary>Every item in the stash, across all pages, in the order the file holds them.</summary>
    public ItemList Items { get; }

    /// <summary>The stat-id width this file turned out to use.</summary>
    public int StatIdBits { get; }

    /// <summary>
    /// Whether the file is in last season's encoding - read, and so written back, with the tables'
    /// previous-season columns: a stash is never moved to another season's encoding by being saved.
    /// </summary>
    public bool LastSeason { get; }

    /// <summary>The page an item sits on, or null when it names none.</summary>
    public static int? PageOf(Item item) =>
        (int)item.Location is var page and >= FirstPage ? page : null;

    /// <summary>
    /// Puts an item on a page of the shared stash at a cell. Mirrors
    /// <see cref="ItemMover.Place"/>, but the page goes in <see cref="Item.Location"/>
    /// because that is where this file keeps it.
    /// </summary>
    public static void Place(Item item, int page, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, FirstPage);

        item.Mode = ItemMode.Stored;
        item.Location = (ItemLocation)page;
        item.Page = InventoryLayout.ExpansionStashPage;
        item.X = (byte)x;
        item.Y = (byte)y;
    }

    /// <summary>
    /// Reads a shared stash, choosing the stat-id width that reproduces the file exactly -
    /// the same test <see cref="SaveVerifier.LoadStash"/> applies, and for the same reason:
    /// the wrong width yields different items rather than an error.
    /// </summary>
    public static SharedStash Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>
    /// The same, from bytes already in hand - a session's history puts an earlier state of the
    /// stash back from them. <paramref name="name"/>
    /// is what a refusal calls it.
    /// </summary>
    public static SharedStash Read(byte[] bytes, string name)
    {
        if (bytes.Length < 8)
            throw new InvalidDataException(L($"'{name}' is too short to be a shared stash."));

        int version = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        var shape = Layout.Known.FirstOrDefault(each => each.Version == version)
            ?? throw new InvalidDataException(L($"'{name}': unknown shared stash layout (version {version})."));

        if (bytes.Length < shape.ItemsAt + 4)
            throw new InvalidDataException(L($"'{name}' is too short to be a shared stash."));

        if (bytes[shape.ItemsAt] != 0x4A || bytes[shape.ItemsAt + 1] != 0x4D)
        {
            throw new InvalidDataException(
                L($"'{name}': unknown shared stash layout (no item list at 0x{shape.ItemsAt:X})."));
        }

        var header = bytes[..shape.ItemsAt];
        var tail = bytes[shape.ItemsAt..];

        // The parser's switches put back as they were once this is done (Globals.Use): reading or
        // writing this file never changes how the next file elsewhere is read.
        using var keep = Globals.Use(Globals.Layout);

        // This season's encoding at ten bits, last season's at ten (the first launch of a new season,
        // before the game has rewritten the file), then nine bits for an old stash
        // (the reader had no last-season attempt at all).
        var read = new List<(int Bits, bool LastSeason, ItemList Items)>();
        foreach (var (bits, lastSeason) in ((int, bool)[])[(10, false), (10, true), (9, false)])
        {
            if (lastSeason && !SeasonReading.HasPreviousSeason)
                continue;

            if (Attempt(tail, bits, lastSeason) is { } items)
                read.Add((bits, lastSeason, items));
        }

        if (read.Count == 0)
        {
            throw new InvalidDataException(
                L($"'{name}' not readable in this or last season's encoding."));
        }

        // Read cleanly as this season's and as last season's: the file's own evidence says which,
        // never the order they were tried in.
        var chosen = read[0];
        var now = read.Where(each => each is { Bits: 10, LastSeason: false }).Select(each => each.Items).FirstOrDefault();
        var then = read.Where(each => each is { Bits: 10, LastSeason: true }).Select(each => each.Items).FirstOrDefault();
        if (now is not null && then is not null)
            chosen = SeasonReading.IsLastSeason(name, now.Items ?? [], then.Items ?? []) ? (10, true, then) : (10, false, now);

        Globals.ResetForFile(pd2Character: false, statIdBits: chosen.Bits, previousSeasonStats: chosen.LastSeason);
        return new SharedStash(shape, header, chosen.Items, chosen.Bits, chosen.LastSeason);
    }

    /// <summary>The items, read under one encoding, if they write back to exactly the bytes they came from.</summary>
    private static ItemList? Attempt(byte[] tail, int bits, bool lastSeason)
    {
        try
        {
            Globals.ResetForFile(pd2Character: false, statIdBits: bits, previousSeasonStats: lastSeason);
            using var reader = new BitReader(tail);
            var items = ItemList.Read(reader, StashVersion);

            Globals.ResetForFile(pd2Character: false, statIdBits: bits, previousSeasonStats: lastSeason);
            return ItemList.Write(items, StashVersion).AsSpan().SequenceEqual(tail) ? items : null;
        }
        catch
        {
            // Another encoding is tried before anything is reported.
            return null;
        }
    }

    /// <summary>
    /// The bytes of this stash: the header as it came, our item list, then the size and
    /// checksum put right.
    /// </summary>
    public byte[] Write() => Write(LastSeason);

    /// <summary>
    /// The bytes of this stash in the encoding asked for: last season's or this season's. Only a check
    /// asks for the other one - <c>pd2spc lastseason</c>, which makes a new season's first-launch files
    /// to test against; the editor writes a stash back as it was read (<see cref="Write()"/>).
    /// </summary>
    public byte[] Write(bool lastSeason)
    {
        Items.Count = (ushort)(Items.Items?.Count ?? 0);

        // The parser's switches put back as they were once this is done (Globals.Use): reading or
        // writing this file never changes how the next file elsewhere is read.
        using var keep = Globals.Use(Globals.Layout);
        Globals.ResetForFile(pd2Character: false, statIdBits: StatIdBits, previousSeasonStats: lastSeason);
        var bytes = _header.Concat(ItemList.Write(Items, StashVersion)).ToArray();

        // Same two fields, at the same two offsets, as a character save.
        Header.Fix(bytes);
        return bytes;
    }

    /// <summary>Stashes carry no header version, so items are read at the LoD version.</summary>
    private const uint StashVersion = 0x60;
}
