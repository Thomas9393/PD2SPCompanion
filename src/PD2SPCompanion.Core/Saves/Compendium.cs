using D2SLib;
using D2SLib.IO;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// PD2's Compendium, Season 14 on: the stash's Uniques and Sets tabs, where a player deposits one copy of an item to
/// keep and takes it out again. A file of its own per mode beside the shared stash - <see cref="SoftcoreFile"/>,
/// <see cref="HardcoreFile"/> (names in ProjectDiablo.dll).
/// <para>
/// The format, proven by reading one with ten items deposited in game: <c>55 BC 55 BC</c>, a u32
/// version (1), the file's size and a checksum - the <c>.d2s</c> routine at the same offsets, put right by
/// <see cref="Header.Fix"/> - then at <c>0x10</c> a plain <c>JM</c> item list of <b>whole items</b>, in the order they
/// were deposited. The game sets a deposited item's page to <see cref="Page"/> and leaves the rest of its place as it
/// was (an X of 15, a Location of Neck): neither means anything here. Items of last season's stamp are taken (an S13
/// one was), each read and written by its own stamp (<see cref="Globals.ItemEra"/>).
/// </para>
/// </summary>
public sealed class Compendium
{
    /// <summary>The softcore compendium, as the game names it.</summary>
    public const string SoftcoreFile = "pd2_shared.compendium";

    /// <summary>The hardcore one.</summary>
    public const string HardcoreFile = "pd2_hc_shared.compendium";

    /// <summary>The page every deposited item carries: 7, on all ten read (the shared stash's is 6).</summary>
    public const byte Page = 7;

    /// <summary>Where the item list starts: after the magic, the version, the size and the checksum.</summary>
    private const int ItemsAt = 0x10;

    private static readonly byte[] Magic = [0x55, 0xBC, 0x55, 0xBC];

    private const uint StashVersion = 0x60;

    private readonly byte[] _header;

    private Compendium(byte[] header, ItemList items)
    {
        _header = header;
        List = items;
    }

    /// <summary>The u32 after the magic: 1 in Season 14.</summary>
    public int Version => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(4));

    private ItemList List { get; }

    /// <summary>Every item deposited, in the order the file holds them.</summary>
    public IReadOnlyList<Item> Items => List.Items;

    /// <summary>The file's name in a save folder for a mode.</summary>
    public static string FileFor(bool hardcore) => hardcore ? HardcoreFile : SoftcoreFile;

    /// <summary>The compendium of a mode in a save folder, or null where there is none yet - a player who never opened the tab.</summary>
    public static Compendium? In(string saveFolder, bool hardcore)
    {
        string path = Path.Combine(saveFolder, FileFor(hardcore));
        return File.Exists(path) ? Read(path) : null;
    }

    /// <summary>
    /// A compendium with nothing in it, as the game would begin one - version 1 - for a mode whose file is not there yet.
    /// Its first deposit makes the file.
    /// </summary>
    public static Compendium Empty()
    {
        var header = new byte[ItemsAt];
        Magic.CopyTo(header, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 1);
        return new Compendium(header, new ItemList { Header = 0x4D4A });
    }

    public static Compendium Read(string path) => Read(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>
    /// The same, from bytes in hand. <paramref name="name"/> is what a refusal calls it. Refused unless the whole file
    /// writes back to exactly the bytes it came from - what makes a write of it safe (the patch guard's rule).
    /// </summary>
    public static Compendium Read(byte[] bytes, string name)
    {
        if (bytes.Length < ItemsAt + 4 || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException(L($"'{name}' is not a compendium."));
        if (bytes[ItemsAt] != 0x4A || bytes[ItemsAt + 1] != 0x4D)
            throw new InvalidDataException(L($"'{name}': unknown compendium layout (no item list at 0x{ItemsAt:X})."));

        var tail = bytes[ItemsAt..];

        // The parser's switches put back as they were once this is done: reading this file never changes how the next
        // file elsewhere is read. This season's encoding; an item of last season's stamp reads by its own.
        using var keep = Globals.Use(Globals.Layout);
        Compendium read;
        try
        {
            Globals.ResetForFile(pd2Character: false, statIdBits: 10);
            using var reader = new BitReader(tail);
            read = new Compendium(bytes[..ItemsAt], ItemList.Read(reader, StashVersion));
        }
        catch (Exception problem)
        {
            throw new InvalidDataException(L($"'{name}' not readable: {problem.Message}"), problem);
        }

        if (!read.Write().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException(L($"'{name}': does not read back as it is."));

        return read;
    }

    /// <summary>The bytes of this compendium: the header as it came, the item list, the size and checksum put right.</summary>
    public byte[] Write()
    {
        List.Count = (ushort)List.Items.Count;

        using var keep = Globals.Use(Globals.Layout);
        Globals.ResetForFile(pd2Character: false, statIdBits: 10);
        var bytes = _header.Concat(ItemList.Write(List, StashVersion)).ToArray();

        // The same two fields, at the same two offsets, as a character save (proven on a real file).
        Header.Fix(bytes);
        return bytes;
    }

    /// <summary>
    /// Puts an item in, last, as the game appends one: its page set to <see cref="Page"/>, stored, the rest of its place
    /// left as it lay. The caller has taken it from where it was and asked whether it may come in.
    /// </summary>
    public void Deposit(Item item)
    {
        item.Mode = ItemMode.Stored;
        item.Page = Page;
        List.Items.Add(item);
    }

    /// <summary>Takes an item out, the same object; false where it was not here.</summary>
    public bool TakeOut(Item item) => List.Items.Remove(item);
}
