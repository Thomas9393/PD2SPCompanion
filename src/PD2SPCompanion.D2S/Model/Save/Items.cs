using D2SLib.IO;
using D2SLib.Model.TXT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Text.Json.Serialization;

namespace D2SLib.Model.Save
{
    public enum ItemMode
    {
        Stored = 0x0,
        Equipped = 0x1,
        Belt = 0x2,
        Buffer = 0x4,
        Socket = 0x6,
    }

    public enum ItemLocation
    {
        None,
        Head,
        Neck,
        Torso,
        RightHand,
        LeftHand,
        RightFinger,
        LeftFinger,
        Waist,
        Feet,
        Gloves,
        SwapRight,
        SwapLeft
    }

    public enum ItemQuality
    {
        Inferior = 0x1,
        Normal,
        Superior,
        Magic,
        Set,
        Rare,
        Unique,
        Craft,
        Tempered
    }

    public class ItemList
    {
        public UInt16? Header { get; set; }
        public UInt16 Count { get; set; }
        public List<Item> Items { get; set; } = new List<Item>();

        public static ItemList Read(BitReader reader, UInt32 version)
        {
            bool writeConsole = Globals.writeConsole_ItemsRead;
            if (writeConsole) Console.Write("ItemList... ");
            ItemList itemList = new ItemList();
            if (!Globals.pd2_stash_formatting) {
                itemList.Header = reader.ReadUInt16();  // online pd2 stashes don't use header
            }
            itemList.Count = reader.ReadUInt16();
            if (writeConsole) Console.Write(itemList.Count + " items... ===================================\r\n");
            for (int i = 0; i < itemList.Count; i++)
            {
                if (writeConsole) Console.WriteLine("Item-" + i + "... ");
                itemList.Items.Add(Item.Read(reader, version));
                // Quantity is left exactly as the file gave it. It used to be forced to 1
                // when zero, which wrote a different item back - and only for items in this
                // list, so the same base read differently once socketed into another.
                if (writeConsole) Console.WriteLine("Item-" + i + " __________________");
            }
            return itemList;
        }

        public static byte[] Write(ItemList itemList, UInt32 version)
        {
            using (BitWriter writer = new BitWriter())
            {
                if (version <= 0x60 && !Globals.pd2_stash_formatting)
                {
                    writer.WriteUInt16(itemList.Header ?? (UInt16)0x4D4A);  // D2 LoD, online pd2 stashes don't use header
                }
                writer.WriteUInt16(itemList.Count);
                for (int i = 0; i < itemList.Count; i++)
                {
                    writer.WriteBytes(Item.Write(itemList.Items[i], version));
                }
                return writer.ToArray();
            }
        }

    }

    public class Item
    {
        public UInt16? Header { get; set; }
        //[JsonIgnore]
        private BitArray? _flags;

        /// <summary>
        /// The item's 32 flag bits.
        /// <para>
        /// <b>Never null, deliberately.</b> Every flag on this class is a bare
        /// <c>Flags[n]</c>, so a null array turned <c>IsCompact</c> - the first line of
        /// <c>Legitimacy.Check</c> and of most rendering - into a
        /// <see cref="NullReferenceException"/>. Worse, the writer's own recovery for it
        /// read <c>item.IsIdentified</c> to rebuild the array, which reads <c>Flags[4]</c>,
        /// so the null branch could only ever throw.
        /// </para>
        /// <para>
        /// An item off a file always has these; an item built in memory did not always, and
        /// the origin of that was never found. Materialising an empty set on demand costs
        /// nothing, reads as "no flags set" - which is the truthful answer for an item that
        /// has not been given any - and removes the whole class of failure rather than the
        /// one path that was caught. Assigning null is honoured as "reset".
        /// </para>
        /// </summary>
        public BitArray Flags
        {
            get => _flags ??= new BitArray(32);
            set => _flags = value;
        }
        public string Version { get; set; }
        public ItemMode Mode { get; set; }
        public ItemLocation Location { get; set; }
        public byte X { get; set; }
        public byte Y { get; set; }
        public byte Page { get; set; }  // is this PlugY-only?
        public byte EarLevel { get; set; }
        public string PlayerName { get; set; }  //used for personalized or ears
        public string Code { get; set; }
        /// <summary>
        /// How many items sit in this one's sockets - <b>on an extended item</b>. On a
        /// compact one the same bit means something else entirely; see
        /// <see cref="CarriesQuestFlag"/>.
        /// </summary>
        public byte NumberOfSocketedItems { get; set; }

        /// <summary>
        /// The <b>three</b> bits every item carries after its code. On a compact item they
        /// are <b>not</b> a socket count - a compact item is a rune, a potion, a scroll, and
        /// none of those has sockets - but a quest state.
        /// <para>
        /// Measured across 3356 compact items in a live S13 character and the item pack:
        /// all but a handful are zero, and the ones that are not are quest items - Mephisto
        /// SoulStones (<c>mss</c>) and a deciphered Bark Scroll (<c>bkd</c>, the Key to the
        /// Cairn Stones).
        /// </para>
        /// <para>
        /// <b>The field is three bits wide here too, not one.</b> Reading one bit looked
        /// right for a long time because a top-level item is byte-aligned, so the bits an
        /// under-read leaves behind are written back as padding zeroes and nothing downstream
        /// shifts - it is silent until one of them is actually set. A live SpearZon carrying
        /// a Mephisto SoulStone whose value is <b>2</b> (bit 93, not bit 92) failed to
        /// round-trip on exactly that byte, and the editor refused the character. Two
        /// distinct non-zero values, 1 and 2, is also why this is a state and not a flag.
        /// Widening it to three bits changed nothing across 335 corpus saves.
        /// </para>
        /// <para>
        /// It is read and written through <see cref="NumberOfSocketedItems"/> so the bytes
        /// are untouched, which is why this is a property rather than a field. What must
        /// not happen is recomputing that number from the socketed items of a compact item:
        /// it would write zero over this flag and quietly change a quest item.
        /// </para>
        /// </summary>
        public bool CarriesQuestFlag => IsCompact && NumberOfSocketedItems != 0;
        public byte TotalNumberOfSockets { get; set; }
        public List<Item> SocketedItems { get; set; } = new List<Item>();
        public UInt32 Id { get; set; }
        public byte ItemLevel { get; set; }
        public ItemQuality Quality { get; set; }
        public bool HasMultipleGraphics { get; set; }
        public byte GraphicId { get; set; }
        public bool IsAutoAffix { get; set; }
        public UInt16 AutoAffixId { get; set; } //?
        public UInt32 FileIndex { get; set; }

        /// <summary>
        /// Four bits Season 14 writes after an <b>unidentified unique</b>'s row id, and on no other item - seen on a
        /// character: Ichorsting, Wall of the Eyeless and Stormspire, dropped in game unidentified, each
        /// with four zero bits there; identified uniques (Arkaine's Valor, the Atma's Scarabs) and an unidentified magic
        /// jewel have none. Read as twelve bits, every bit after them shifted: two of the three did not read, the third read
        /// as garbage. What they mean is unknown; kept as read. Written only while the item is unidentified
        /// (<see cref="HasUnidentifiedTail"/>), so an item identified loses them - the shape every identified unique has,
        /// and what the game does: Ichorsting, identified in game the same evening, read back without them. A set item
        /// carries them too: an ethereal Trang-Oul's Claws dropped unidentified did not read without them (a
        /// second specimen).
        /// </summary>
        public byte UnidentifiedTail { get; set; }

        /// <summary>The stamp <see cref="UnidentifiedTail"/> begins at: Season 14's.</summary>
        public const int UnidentifiedTailSince = 105;

        /// <summary>Whether the item carries <see cref="UnidentifiedTail"/>: a unique or set item, unidentified, of Season 14's stamp (105) or later.</summary>
        public static bool HasUnidentifiedTail(Item item) =>
            item.Quality is ItemQuality.Unique or ItemQuality.Set && !item.IsIdentified
            && int.TryParse(item.Version, out int stamp) && stamp >= UnidentifiedTailSince;

        public UInt16[] MagicPrefixIds { get; set; } = new UInt16[3];
        public UInt16[] MagicSuffixIds { get; set; } = new UInt16[3];
        public UInt16 RarePrefixId { get; set; }
        public UInt16 RareSuffixId { get; set; }
        public UInt32 RunewordId { get; set; }
        /// <summary>
        /// The four bits after the runeword id, which pick the slot its property list is
        /// read into. The game writes 5; upstream wrote 5 back whatever was read, which
        /// rewrote any item holding another value - one in BetweenWalls' item pack holds 2.
        /// </summary>
        public byte RunewordListSlot { get; set; } = 5;
        //[JsonIgnore]
        public bool HasRealmData { get; set; }
        //[JsonIgnore]
        public UInt32[] RealmData { get; set; } = new UInt32[3];
        public UInt16 Armor { get; set; }
        public UInt16 MaxDurability { get; set; }
        public UInt16 Durability { get; set; }
        /// <summary>Unknown single bit after durability. Preserved so writes stay faithful.</summary>
        public bool DurabilitySpareBit { get; set; }
        public UInt16 Quantity { get; set; }
        public byte SetItemMask { get; set; }
        public List<ItemStatList> StatLists { get; set; } = new List<ItemStatList>();
        public bool IsIdentified {  get { return Flags[4]; } set { Flags[4] = value;  } }
        public bool IsSocketed { get { return Flags[11]; } set { Flags[11] = value; } }
        public bool IsNew { get { return Flags[13]; } set { Flags[13] = value; } }
        public bool IsEar { get { return Flags[16]; } set { Flags[16] = value; } }
        public bool IsStarterItem { get { return Flags[17]; } set { Flags[17] = value; } }
        public bool IsCompact { get { return Flags[21]; } set { Flags[21] = value; } }
        public bool IsEthereal { get { return Flags[22]; } set { Flags[22] = value; } }
        public bool IsPersonalized { get { return Flags[24]; } set { Flags[24] = value; } }
        public bool IsRuneword { get { return Flags[26]; } set { Flags[26] = value; } }

        public static Item Read(byte[] bytes, UInt32 version)
        {
            using(BitReader reader = new BitReader(bytes))
            {
                return Read(reader, version);
            }
        }

        public static Item Read(BitReader reader, UInt32 version)
        {
            bool writeConsole = Globals.writeConsole_ItemsReadComplete;
            Item item = new Item();
            if (version <= 0x60) item.Header = reader.ReadUInt16();
            ReadCompact(reader, item, version);
            if(!item.IsCompact)
            {
                // Its properties in its own season's encoding, by the stamp just read (Globals.ItemEra).
                using (Globals.ItemEra(item.Version))
                    ReadComplete(reader, item, version);
            }
            reader.Align();

            for(int i = 0; i < SocketedItemsOf(item); i++)
            {
                item.SocketedItems.Add(Read(reader, version));
            }
            return item;
        }

        /// <summary>
        /// How many items are socketed into this one - which is <b>none</b> when it is
        /// compact.
        /// <para>
        /// A compact item is a rune, a potion, a scroll: it has no quality, no item level and
        /// no sockets, because everything that can be socketed carries the extended block.
        /// The field read after its code is one bit rather than three, and it is not a socket
        /// count. Ordinarily that costs nothing, because <c>reader.Align()</c> swallows the
        /// bit before the next item starts - but when the bit happens to be <b>1</b>, the
        /// reader went looking for a socketed item and ate the next real one.
        /// </para>
        /// <para>
        /// Seen on a live S13 character: a <c>bkd</c>, the Key to the Cairn Stones, read as
        /// holding one socketed item and swallowed the Nadir cap stored after it, along with
        /// the two runes in that cap. Three top-level items vanished into one, the list ran
        /// three short of its own count, and the read overshot the end into the corpse
        /// section and failed on garbage. The character had been perfectly readable until the
        /// quest item was picked up.
        /// </para>
        /// <para>
        /// The bit is still read and written exactly as it was, so the bytes are unchanged;
        /// only what the reader does about it has.
        /// </para>
        /// </summary>
        private static int SocketedItemsOf(Item item) =>
            item.IsCompact ? 0 : item.NumberOfSocketedItems;

        public static byte[] Write(Item item, UInt32 version)
        {
            using (BitWriter writer = new BitWriter())
            {
                writer.WriteUInt16(item.Header ?? (UInt16)0x4D4A);
                WriteCompact(writer, item, version);
                if (!item.IsCompact)
                {
                    using (Globals.ItemEra(item.Version))
                        WriteComplete(writer, item, version);
                }
                writer.Align();
                for (int i = 0; i < SocketedItemsOf(item); i++)
                {
                    writer.WriteBytes(Item.Write(item.SocketedItems[i], version));
                }
                return writer.ToArray();
            }
        }
        
        public static byte[] Write(Item item, UInt32 version, BitWriter writer) // added for PD2 conversion
        {
            writer.WriteUInt16(item.Header ?? (UInt16)0x4D4A);
            WriteCompact(writer, item, version);
            if (!item.IsCompact)
            {
                using (Globals.ItemEra(item.Version))
                    WriteComplete(writer, item, version);
            }
            writer.Align();
            for (int i = 0; i < SocketedItemsOf(item); i++)
            {
                writer.WriteBytes(Item.Write(item.SocketedItems[i], version));
            }
            return writer.ToArray();
        }

        // An ear's or a personalized item's name is 7-bit characters up to a 7-bit null, at
        // most 15 of them before it. Upstream read 7 bits but wrote 8, and wrote back the
        // unused padding as extra nulls, so any such item grew on every save.
        protected static string ReadPlayerName(BitReader reader)
        {
            var name = new StringBuilder(15);
            for (int i = 0; i < 16; i++)
            {
                char c = (char)reader.ReadByte(7);
                if (c == '\0')
                {
                    break;
                }
                name.Append(c);
            }
            return name.ToString();
        }

        protected static void WritePlayerName(BitWriter writer, string name)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(name.TrimEnd('\0'));
            for (int i = 0; i < bytes.Length; i++)
            {
                writer.WriteByte(bytes[i], 7);
            }
            writer.WriteByte(0, 7);
        }

        protected static void ReadCompact(BitReader reader, Item item, UInt32 version)
        {
            bool writeConsole = Globals.writeConsole_ItemsRead;
            item.Flags = new BitArray(reader.ReadBytes(4));
            if(version <= 0x60) item.Version = Convert.ToString(reader.ReadUInt16(10), 10);     // D2 LoD
            else if(version >= 0x61) item.Version = Convert.ToString(reader.ReadUInt16(3), 2);  // D2 Resurrected
            item.Mode = (ItemMode)reader.ReadByte(3);
            item.Location = (ItemLocation)reader.ReadByte(4);
            item.X = reader.ReadByte(4);
            item.Y = reader.ReadByte(4);
            if (writeConsole) Console.WriteLine($"Item-X,Y: {item.X + 1},{item.Y + 1}");
            item.Page = reader.ReadByte(3);
            if (item.IsEar)
            {
                if (writeConsole) Console.WriteLine("Item-Code: ear?");
                item.FileIndex = reader.ReadByte(3);
                item.EarLevel = reader.ReadByte(7);
                item.PlayerName = ReadPlayerName(reader);
            }
            else
            {
                item.Code = "";
                if (version <= 0x60) item.Code = reader.ReadString(4);      // D2 LoD
                else if (version >= 0x61)                                   // D2 Resurrected
                {
                    for(int i = 0; i < 4; i++)
                    {
                        item.Code += Core.TXT.ItemsTXT.ItemCodeTree.DecodeChar(reader);
                    }
                }
                item.NumberOfSocketedItems = reader.ReadByte(3);
            }
            if (writeConsole) Console.WriteLine($"Item-Code: {item.Code} (compact {item.IsCompact}, socketed {item.NumberOfSocketedItems})");
            //if (writeConsole) Console.WriteLine($"Item-Compact: {item.IsCompact}");
        }

        protected static void WriteCompact(BitWriter writer, Item item, UInt32 version)
        {
            // Was preceded by a null check that rebuilt the array from IsIdentified,
            // IsSocketed and the rest - every one of which reads the array it was trying to
            // replace, so the branch could only throw. Flags materialises itself now.
            BitArray flags = item.Flags;

            // By index: Cast<bool> boxed each of the 32.
            for (int i = 0; i < flags.Length; i++)
            {
                writer.WriteBit(flags[i]);
            }
            if (version <= 0x60)
            {
                //todo. how do we handle 1.15 version to 1.14. maybe this should be a string
                writer.WriteUInt16(Convert.ToUInt16(item.Version, 10), 10);
            }
            else if (version >= 0x61)
            {
                writer.WriteUInt16(Convert.ToUInt16(item.Version, 2), 3);
            }
            writer.WriteByte((byte)item.Mode, 3);
            writer.WriteByte((byte)item.Location, 4);
            writer.WriteByte(item.X, 4);
            writer.WriteByte(item.Y, 4);
            writer.WriteByte(item.Page, 3);
            if(item.IsEar)
            {
                writer.WriteUInt32(item.FileIndex, 3);
                writer.WriteByte(item.EarLevel, 7);
                WritePlayerName(writer, item.PlayerName);
            }
            else
            {
                var code = Encoding.ASCII.GetBytes(item.Code.PadRight(4, ' '));
                if (version <= 0x60)
                {
                    writer.WriteBytes(code);
                }
                else if (version >= 0x61)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        BitArray bits = Core.TXT.ItemsTXT.ItemCodeTree.EncodeChar((char)code[i]);
                        for (int b = 0; b < bits.Length; b++)
                        {
                            writer.WriteBit(bits[b]);
                        }
                    }
                }
                writer.WriteByte(item.NumberOfSocketedItems, 3);
            }

        }

        protected static void ReadComplete(BitReader reader, Item item, UInt32 version)
        {
            bool writeConsole = Globals.writeConsole_ItemsReadComplete;
            item.Id = reader.ReadUInt32();
            item.ItemLevel = reader.ReadByte(7);
            if (writeConsole) Console.WriteLine("Item-ItemLevel: " + item.ItemLevel);
            item.Quality = (ItemQuality)reader.ReadByte(4);
            if (writeConsole) Console.WriteLine("Item-Quality: " + item.Quality);
            item.HasMultipleGraphics = reader.ReadBit();
            if (item.HasMultipleGraphics)
            {
                item.GraphicId = reader.ReadByte(3);
            }
            item.IsAutoAffix = reader.ReadBit();
            if (item.IsAutoAffix)
            {
                item.AutoAffixId = reader.ReadUInt16(11);
            }
            switch(item.Quality)
            {
                case ItemQuality.Normal:
                    break;
                case ItemQuality.Inferior:
                case ItemQuality.Superior:
                    item.FileIndex = reader.ReadUInt16(3);
                    //if (writeConsole) Console.WriteLine("Item-FileIndex: " + item.FileIndex);
                    break;
                case ItemQuality.Magic:
                    item.MagicPrefixIds[0] = reader.ReadUInt16(11);
                    item.MagicSuffixIds[0] = reader.ReadUInt16(11);
                    break;
                case ItemQuality.Rare:
                case ItemQuality.Craft:
                    item.RarePrefixId = reader.ReadUInt16(8);
                    item.RareSuffixId = reader.ReadUInt16(8);
                    for (int i = 0; i < 3; i++)
                    {
                        if(reader.ReadBit())
                        {
                            item.MagicPrefixIds[i] = reader.ReadUInt16(11);
                        }
                        if(reader.ReadBit())
                        {
                            item.MagicSuffixIds[i] = reader.ReadUInt16(11);
                        }
                    }
                    break;
                case ItemQuality.Set:
                case ItemQuality.Unique:
                    item.FileIndex = reader.ReadUInt16(12);
                    if (HasUnidentifiedTail(item))
                        item.UnidentifiedTail = reader.ReadByte(4);
                    //if (writeConsole) Console.WriteLine("Item-FileIndex: " + item.FileIndex);
                    break;
            }
            UInt16 propertyLists = 0;
            if(item.IsRuneword)
            {
                item.RunewordId = reader.ReadUInt32(12);
                if (writeConsole) Console.WriteLine("Item-RunewordId: " + item.RunewordId);
                item.RunewordListSlot = reader.ReadByte(4);
                propertyLists |= (UInt16)(1 << (item.RunewordListSlot + 1));
            }
            if (item.IsPersonalized)
            {
                item.PlayerName = ReadPlayerName(reader);
                if (writeConsole) Console.WriteLine("Item-PlayerName: " + item.PlayerName);
            }
            if (item.Code == null)
            {
                if (Globals.writeConsole_ItemsReadComplete) Console.Write("!");
                throw new Exception("invalid item data");
            }

            if (item.Code.Contains("?"))        // TODO: Check characters? Should only contain alphanumeric characters
            {
                if (Globals.writeConsole_ItemsReadComplete) Console.Write("!");
                throw new Exception("invalid item data");
            }
            if (item.Code.Trim() == "tbk" || item.Code.Trim() == "ibk")
                {
                    item.MagicSuffixIds[0] = reader.ReadByte(5);
                }
            item.HasRealmData = reader.ReadBit();
            if (item.HasRealmData)
            {
                reader.ReadBits(96);
                //if (writeConsole) Console.WriteLine("Item-RealmData: " + item.RealmData);
            }
            ItemStatCostTXT itemStatCostTXT = Core.TXT.ItemStatCostTXT;
            TXTRow row = Core.TXT.ItemsTXT.GetByCode(item.Code);
            bool isArmor = Core.TXT.ItemsTXT.IsArmor(item.Code);
            bool isWeapon = Core.TXT.ItemsTXT.IsWeapon(item.Code);
            bool isMisc = Core.TXT.ItemsTXT.IsMisc(item.Code);
            bool isStackable = false;
            if (isArmor || isWeapon || isMisc) isStackable = row["stackable"].ToBool();
            if (isArmor)
            {
                //why do i need this cast?
                // Stored as (value + Save Add); subtract to get what the game shows.
                item.Armor = (UInt16)(reader.ReadUInt16(11) - itemStatCostTXT["armorclass"]["Save Add"].ToUInt16());
                //if (writeConsole) Console.WriteLine("Item-Armor: " + item.Armor);
            }
            if(isArmor || isWeapon)
            {
                var maxDurabilityStat = itemStatCostTXT["maxdurability"];
                // Deliberately maxdurability for both. The pair on an item is eight bits
                // each; the durability row is nine, which is the width of a durability
                // bonus an affix grants, not of this field. Pointing this at "durability"
                // looks like a correction and desyncs every bit after it.
                var durabilityStat = itemStatCostTXT["maxdurability"];
                item.MaxDurability = (UInt16)(reader.ReadUInt16(maxDurabilityStat["Save Bits"].ToInt32()) - maxDurabilityStat["Save Add"].ToUInt16());
                //if (writeConsole) Console.WriteLine("Item-MaxDurability: " + item.MaxDurability);
                if (item.MaxDurability > 0)
                {
                    item.Durability = (UInt16)(reader.ReadUInt16(durabilityStat["Save Bits"].ToInt32()) + durabilityStat["Save Add"].ToUInt16());
                    //if (writeConsole) Console.WriteLine("Item-Durability: " + item.Durability);
                    item.DurabilitySpareBit = reader.ReadBit();
                }
            }
            if(isStackable)
            {
                item.Quantity = reader.ReadUInt16(9);
                if (writeConsole) Console.WriteLine("Item-Quantity: " + item.Quantity);
            }
            if(item.IsSocketed)
            {
                item.TotalNumberOfSockets = reader.ReadByte(4);
                if (writeConsole) Console.WriteLine("Item-TotalNumberOfSockets: " + item.TotalNumberOfSockets);
            }
            item.SetItemMask = 0;
            if(item.Quality == ItemQuality.Set)
            {
                item.SetItemMask = reader.ReadByte(5);
                //if (writeConsole) Console.WriteLine("Item-SetItemMask: " + item.SetItemMask);
                propertyLists |= item.SetItemMask;
            }
            item.StatLists.Add(ItemStatList.Read(reader));
            for(int i = 1; i <= 64; i<<=1)
            {
                if((propertyLists & i) != 0)
                {
                    item.StatLists.Add(ItemStatList.Read(reader));
                }
            }
        }

        protected static void WriteComplete(BitWriter writer, Item item, UInt32 version)
        {
            writer.WriteUInt32(item.Id);
            writer.WriteByte(item.ItemLevel, 7);
            writer.WriteByte((byte)item.Quality, 4);
            writer.WriteBit(item.HasMultipleGraphics);
            if (item.HasMultipleGraphics)
            {
                writer.WriteByte(item.GraphicId, 3);
            }
            writer.WriteBit(item.IsAutoAffix);
            if (item.IsAutoAffix)
            {
                writer.WriteUInt16(item.AutoAffixId, 11);
            }
            switch (item.Quality)
            {
                case ItemQuality.Normal:
                    break;
                case ItemQuality.Inferior:
                case ItemQuality.Superior:
                    writer.WriteUInt32(item.FileIndex, 3);
                    break;
                case ItemQuality.Magic:
                    writer.WriteUInt16(item.MagicPrefixIds[0], 11);
                    writer.WriteUInt16(item.MagicSuffixIds[0], 11);
                    break;
                case ItemQuality.Rare:
                case ItemQuality.Craft:
                    writer.WriteUInt16(item.RarePrefixId, 8);
                    writer.WriteUInt16(item.RareSuffixId, 8);
                    for (int i = 0; i < 3; i++)
                    {
                        var hasPrefix = item.MagicPrefixIds[i] > 0;
                        var hasSuffix = item.MagicSuffixIds[i] > 0;
                        writer.WriteBit(hasPrefix);
                        if (hasPrefix)
                        {
                            writer.WriteUInt16(item.MagicPrefixIds[i], 11);
                        }
                        writer.WriteBit(hasSuffix);
                        if (hasSuffix)
                        {
                            writer.WriteUInt16(item.MagicSuffixIds[i], 11);
                        }
                    }
                    break;
                case ItemQuality.Set:
                case ItemQuality.Unique:
                    writer.WriteUInt32(item.FileIndex, 12);
                    if (HasUnidentifiedTail(item))
                        writer.WriteUInt32(item.UnidentifiedTail, 4);
                    break;
            }
            UInt16 propertyLists = 0;
            if (item.IsRuneword)
            {
                writer.WriteUInt32(item.RunewordId, 12);
                propertyLists |= (UInt16)(1 << (item.RunewordListSlot + 1));
                writer.WriteByte(item.RunewordListSlot, 4);
            }
            if (item.IsPersonalized)
            {
                WritePlayerName(writer, item.PlayerName);
            }
            if (item.Code.Trim() == "tbk" || item.Code.Trim() == "ibk")
            {
                writer.WriteUInt16(item.MagicSuffixIds[0], 5);
            }
            writer.WriteBit(item.HasRealmData);
            if (item.HasRealmData)
            {
                //todo 96 bits
            }
            ItemStatCostTXT itemStatCostTXT = Core.TXT.ItemStatCostTXT;
            TXTRow row = Core.TXT.ItemsTXT.GetByCode(item.Code);
            bool isArmor = Core.TXT.ItemsTXT.IsArmor(item.Code);
            bool isWeapon = Core.TXT.ItemsTXT.IsWeapon(item.Code);
            bool isMisc = Core.TXT.ItemsTXT.IsMisc(item.Code);
            bool isStackable = false;
            if (isArmor || isWeapon || isMisc) isStackable = row["stackable"].ToBool();
            if (isArmor)
            {
                writer.WriteUInt16((UInt16)(item.Armor + itemStatCostTXT["armorclass"]["Save Add"].ToUInt16()), 11);
            }
            if (isArmor || isWeapon)
            {
                var maxDurabilityStat = itemStatCostTXT["maxdurability"];
                var durabilityStat = itemStatCostTXT["maxdurability"];
                writer.WriteUInt16((UInt16)(item.MaxDurability + maxDurabilityStat["Save Add"].ToUInt16()), maxDurabilityStat["Save Bits"].ToInt32());
                if (item.MaxDurability > 0)
                {
                    writer.WriteUInt16((UInt16)(item.Durability + durabilityStat["Save Add"].ToUInt16()), durabilityStat["Save Bits"].ToInt32());
                    writer.WriteBit(item.DurabilitySpareBit);
                }
            }
            if (isStackable)
            {
                writer.WriteUInt16(item.Quantity, 9);
            }
            if (item.IsSocketed)
            {
                writer.WriteByte(item.TotalNumberOfSockets, 4);
            }
            if (item.Quality == ItemQuality.Set)
            {
                writer.WriteByte(item.SetItemMask, 5);
                propertyLists |= item.SetItemMask;
            }
            ItemStatList.Write(writer, item.StatLists[0]);
            var idx = 1;
            for (int i = 1; i <= 64; i <<= 1)
            {
                if ((propertyLists & i) != 0)
                {
                    ItemStatList.Write(writer, item.StatLists[idx++]);
                }
            }
        }
    }

    public class ItemStatList
    {
        public List<ItemStat> Stats { get; set; } = new List<ItemStat>();

        public static ItemStatList Read(BitReader reader)
        {
            ItemStatList itemStatList = new ItemStatList();
            if (Globals.writeConsole_ItemsReadComplete) Console.WriteLine($"    [stat list @ bit {reader.Position}]");
            UInt16 id = reader.ReadUInt16(Globals.stat_id_bits);
            while(id != Globals.StatIdTerminator)
            {
                if (Globals.writeConsole_ItemsReadComplete) Console.Write($"    stat id {id} @ bit {reader.Position - 9} -> ");
                itemStatList.Stats.Add(ItemStat.Read(reader, id));
                if (Globals.writeConsole_ItemsReadComplete) { var st = itemStatList.Stats[itemStatList.Stats.Count - 1]; Console.WriteLine($"{st.Stat} = {st.Value} (param {st.Param}), now at bit {reader.Position}"); }
                //https://github.com/ThePhrozenKeep/D2MOO/blob/master/source/D2Common/src/Items/Items.cpp#L7332
                if (Runs.TryGetValue(id, out Int32 followers))
                {
                    for (Int32 step = 1; step <= followers; step++)
                    {
                        itemStatList.Stats.Add(ItemStat.Read(reader, (UInt16)(id + step)));
                    }
                }
                id = reader.ReadUInt16(Globals.stat_id_bits);
            }
            if (Globals.writeConsole_ItemsReadComplete) Console.WriteLine($"    [terminator, now at bit {reader.Position}]");
            return itemStatList;
        }

        /// <summary>
        /// Stats the file stores as a run: the id that leads one, and how many followers are
        /// written after it with no ids of their own. <see cref="Read"/> above is what
        /// defines them and reads from this same table, so the two cannot drift apart.
        /// </summary>
        private static readonly Dictionary<UInt16, Int32> RunFollowers = new Dictionary<UInt16, Int32>
        {
            { 17, 1 },   // item_maxdamage_percent, then item_mindamage_percent
            { 48, 1 },   // firemindam, then firemaxdam
            { 50, 1 },   // lightmindam, then lightmaxdam
            { 52, 1 },   // magicmindam, then magicmaxdam
            { 54, 2 },   // coldmindam, then coldmaxdam and coldlength
            { 57, 2 },   // poisonmindam, then poisonmaxdam and poisonlength
        };

        /// <summary>The runs, for callers that build stat lists and have to respect them.</summary>
        public static IReadOnlyDictionary<UInt16, Int32> Runs
        {
            get { return RunFollowers; }
        }

        /// <summary>The run this id follows, or null for an id that leads one or is in none.</summary>
        public static UInt16? LeaderOf(UInt16 id)
        {
            foreach (var pair in RunFollowers)
            {
                if (id > pair.Key && id <= pair.Key + pair.Value)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        public static void Write(BitWriter writer, ItemStatList itemStatList)
        {
            foreach (var entry in Ordered(itemStatList.Stats))
            {
                writer.WriteUInt16(entry.Id, Globals.stat_id_bits);

                foreach (var stat in entry.Written)
                {
                    ItemStat.Write(writer, stat);
                }
            }

            writer.WriteUInt16(Globals.StatIdTerminator, Globals.stat_id_bits);
        }

        /// <summary>
        /// The list in the order the file needs it: one entry per id the reader will meet,
        /// each carrying the stat written under that id and, for a run, the followers written
        /// with no id of their own.
        /// <para>
        /// Pairing by <b>id</b> rather than by position is the whole point. The reader, on
        /// meeting <c>coldmindam</c>, takes the next two entries as <c>coldmaxdam</c> and
        /// <c>coldlength</c> whatever they actually are, so a list holding those three in any
        /// other order was written as a bit stream that read back as a different item, or as
        /// no item at all. The editor built such lists: every panel that adds a property
        /// appends it, and nothing put the runs back in order.
        /// </para>
        /// <para>
        /// A list the old writer handled correctly is written byte for byte as before, and
        /// that is the check this has to pass - the pack's characters and stashes and the
        /// live save all still write back identical to their bytes on disk. Three cases make
        /// up the difference, and only the first is a change of behaviour:
        /// </para>
        /// <list type="bullet">
        /// <item>A run out of order, or split up, is gathered back together.</item>
        /// <item>A leader missing a follower has it written at zero. The file cannot hold
        /// half a run, and the reader would otherwise take whatever came next as the rest.</item>
        /// <item>A follower with <b>no</b> leader stands on its own under its own id, which is
        /// how the old writer left it and is read back the same way - the reader only reads
        /// ahead on a leader. This is not a broken list and must not be 'corrected'.</item>
        /// </list>
        /// </summary>
        private static List<(UInt16 Id, List<ItemStat> Written)> Ordered(List<ItemStat> stats)
        {
            var order = new List<(UInt16 Id, List<ItemStat> Written)>();

            var ids = new UInt16[stats.Count];
            for (Int32 i = 0; i < stats.Count; i++)
            {
                // The id a stat carries is its row's ID; only a stat known by name is looked up.
                ids[i] = stats[i].Id is { } own ? (UInt16)own : ItemStat.GetStatRow(stats[i])["ID"].ToUInt16();
            }

            var claimed = new bool[stats.Count];

            // The first stat with this id that no entry has taken yet, or -1. Counting from
            // the front keeps a list that is already in order writing exactly as it did:
            // a leader finds the follower sitting right behind it, and a second copy of a
            // run finds the second copy of its followers.
            Func<UInt16, Int32> unclaimed = want =>
            {
                for (Int32 i = 0; i < stats.Count; i++)
                {
                    if (!claimed[i] && ids[i] == want)
                    {
                        return i;
                    }
                }

                return -1;
            };

            for (Int32 i = 0; i < stats.Count; i++)
            {
                if (claimed[i])
                {
                    continue;
                }

                UInt16? follows = LeaderOf(ids[i]);

                // A follower waits for its leader to take it, wherever in the list that
                // leader sits. With no leader left to take it, it stands on its own.
                if (follows.HasValue && unclaimed(follows.Value) >= 0)
                {
                    continue;
                }

                claimed[i] = true;

                var written = new List<ItemStat> { stats[i] };
                Int32 count;

                if (RunFollowers.TryGetValue(ids[i], out count))
                {
                    for (Int32 step = 1; step <= count; step++)
                    {
                        UInt16 member = (UInt16)(ids[i] + step);
                        Int32 held = unclaimed(member);

                        if (held >= 0)
                        {
                            claimed[held] = true;
                            written.Add(stats[held]);
                        }
                        else
                        {
                            written.Add(Filler(member));
                        }
                    }
                }

                order.Add((ids[i], written));
            }

            // A follower skipped for a leader that then took a different copy of it. Nothing
            // is dropped on the way out of here.
            for (Int32 i = 0; i < stats.Count; i++)
            {
                if (!claimed[i])
                {
                    order.Add((ids[i], new List<ItemStat> { stats[i] }));
                }
            }

            return order;
        }

        /// <summary>A run member the list does not hold, at the one value that adds nothing.</summary>
        private static ItemStat Filler(UInt16 id)
        {
            TXTRow row = Core.TXT.ItemStatCostTXT[id];

            return new ItemStat
            {
                Id = id,
                Stat = row == null ? null : row["Stat"].Value,
                Value = 0,
            };
        }

    }

    public class ItemStat
    {
        public UInt16? Id { get; set; }
        public string? Stat { get; set; }
        public Int32? SkillTab { get; set; }

        /// <summary>
        /// The class a skill-tab stat applies to, packed above the tab in the same param.
        /// Not SkillLevel, which is a different thing entirely.
        /// <para>
        /// The bit positions are inherited, not confirmed: nothing seen so far carries this
        /// stat. They round-trip, since read and write are inverses either way.
        /// </para>
        /// </summary>
        public Int32? CharacterClass { get; set; }
        public Int32? SkillId { get; set; }
        public Int32? SkillLevel { get; set; }
        public Int32? MaxCharges { get; set; }
        public Int32? Param { get; set; }
        public Int32 Value { get; set; }

        public static ItemStat Read(BitReader reader, UInt16 id)
        {
            ItemStat itemStat = new ItemStat();
            TXTRow property = Core.TXT.ItemStatCostTXT[id];
            if (property == null)
            {
                throw new Exception($"No ItemStatCost record found for id: {id} at bit {reader.Position - 9}");
            }
            itemStat.Id = id;
            itemStat.Stat = property["Stat"].Value;

            // Not property["Save Param Bits"] directly: the table also carries the previous
            // season's encoding, and which one applies depends on the save being read. See
            // StatEncoding.
            Int32 saveParamBitCount = StatEncoding.SaveParamBits(property);
            Int32 encode = property["Encode"].ToInt32();
            if (saveParamBitCount != 0)
            {
                Int32 saveParam = reader.ReadInt32(saveParamBitCount);
                //todo is there a better way to identify skill tab stats.
                switch (property["descfunc"].ToInt32())
                {
                    // descfunc is what declares a stat's shape. The wording it stands
                    // for - "+[value] to [skilltab] Skill Levels ([class] Only)" - names
                    // three things, so three is how many fields there are. Nothing else in
                    // itemstatcost says it: Save Param Bits gives the room, not the layout.
                    case 14: //+[value] to [skilltab] Skill Levels ([class] Only) : stat id 188
                        // Three fields, not two: which class, which of its three tabs, and
                        // how many levels - and the levels are the stat's Value, not part
                        // of the param. The field above the tab is therefore the class,
                        // which this read called SkillLevel. The split is unchanged and
                        // still round-trips; only the name was wrong, and a field read
                        // under the wrong name is a wrong value waiting to be shown.
                        itemStat.SkillTab = saveParam & 0x7;
                        itemStat.CharacterClass = (saveParam >> 3) & 0x1fff;
                        break;
                    default:
                        break;
                }
                switch (encode)
                {
                    case 2: //chance to cast skill
                    case 3: //skill charges
                        itemStat.SkillLevel = saveParam & 0x3f;
                        itemStat.SkillId = (saveParam >> 6) & 0x3ff;
                        break;
                    case 1:
                    case 4: //by times
                    default:
                        itemStat.Param = saveParam;
                        break;
                }
            }
            Int32 saveBits = reader.ReadInt32(StatEncoding.SaveBits(property));
            saveBits -= StatEncoding.SaveAdd(property);
            switch (encode)
            {
                case 3: //skill charges
                    itemStat.MaxCharges = (saveBits >> 8) & 0xff;
                    itemStat.Value = saveBits & 0xff;
                    break;
                default:
                    itemStat.Value = saveBits;
                    break;
            }
            return itemStat;
        }

        public static void Write(BitWriter writer, ItemStat stat)
        {
            TXTRow property = GetStatRow(stat);
            if (property == null)
            {
                throw new Exception($"No ItemStatCost record found for id: {stat.Id}");
            }
            Int32 saveParamBitCount = StatEncoding.SaveParamBits(property);
            Int32 encode = property["Encode"].ToInt32();
            if (saveParamBitCount != 0)
            {
                if(stat.Param != null)
                {
                    writer.WriteInt32((Int32)stat.Param, saveParamBitCount);
                } else
                {
                    // The masks below bind as written: (x ?? 0) & MASK. Spelled without
                    // the brackets, C# reads `x ?? 0 & MASK` as `x ?? (0 & MASK)`, because
                    // & binds tighter than ??, and every mask becomes a no-op. That is
                    // invisible while the values come from the file - they are already in
                    // range - and corrupts the neighbouring packed field the moment one is
                    // edited to something larger.
                    Int32 saveParamBits = 0;
                    switch (property["descfunc"].ToInt32())
                    {
                        case 14: //+[value] to [skilltab] Skill Levels ([class] Only) : stat id 188
                            saveParamBits |= (stat.SkillTab ?? 0) & 0x7;
                            saveParamBits |= ((stat.CharacterClass ?? 0) & 0x1fff) << 3;
                            break;
                        default:
                            break;
                    }
                    switch (encode)
                    {
                        case 2: //chance to cast skill
                        case 3: //skill charges
                            saveParamBits |= (stat.SkillLevel ?? 0) & 0x3f;
                            saveParamBits |= ((stat.SkillId ?? 0) & 0x3ff) << 6;
                            break;
                        case 4: //by times
                        case 1:
                        default:
                            break;
                    }
                    //always use param if it is there.
                    if (stat.Param != null)
                    {
                        saveParamBits = (Int32)stat.Param;
                    }
                    writer.WriteInt32(saveParamBits, saveParamBitCount);
                }
            }
            Int32 saveBits = stat.Value;
            saveBits += StatEncoding.SaveAdd(property);
            switch (encode)
            {
                case 3: //skill charges
                    saveBits &= 0xff;
                    saveBits |= ((stat.MaxCharges ?? 0) & 0xff) << 8;
                    break;
                default:
                    break;
            }
            writer.WriteInt32(saveBits, StatEncoding.SaveBits(property));
        }
        public static TXTRow GetStatRow(ItemStat stat)
        {
            if (stat.Id != null)
            {
                return Core.TXT.ItemStatCostTXT[(UInt16)stat.Id];
            }
            else
            {
                return Core.TXT.ItemStatCostTXT[(string)stat.Stat];
            }
        }

    }
}
