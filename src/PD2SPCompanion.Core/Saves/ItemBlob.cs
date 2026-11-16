using System.Security.Cryptography;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// One item as a standalone run of bytes - the unit the vault stores.
/// <para>
/// This is the <c>.d2i</c> idea that Hero Editor and GoMule made familiar: an item's own
/// bitstream, <c>JM</c> header and socketed items included, with nothing of the save around
/// it. <see cref="Item.Write(Item, uint)"/> already produces exactly that and
/// <see cref="Item.Read(D2SLib.IO.BitReader, uint)"/> reads it back symmetrically at
/// <see cref="Version"/>.
/// </para>
/// </summary>
public static class ItemBlob
{
    /// <summary>
    /// The save version every item is written at. PD2's <c>.d2s</c> header says <b>96</b>,
    /// which is <c>0x60</c>, and both the reader and the writer branch on
    /// <c>version &lt;= 0x60</c> for the per-item header - so the two agree only at this
    /// value. <see cref="SharedStash"/> uses the same constant for the same reason.
    /// </summary>
    public const uint Version = 0x60;

    /// <summary>
    /// Strips where the item was sitting, leaving what the item <b>is</b>.
    /// <para>
    /// A blob carries <see cref="Item.Id"/> and the position fields because the format has
    /// fixed slots for them and no way to say "unset" - which is why an exported
    /// <c>.d2i</c> from any other editor carries whatever cell the item happened to occupy.
    /// That is harmless for a tool that pastes one item at a time.
    /// </para>
    /// <para>
    /// <b>It is not harmless for a library that deduplicates by content.</b> Two captures of
    /// the same Shako from two different inventory cells would hash differently and store as
    /// two templates. Normalising first is what makes a hash mean "the same item" rather
    /// than "the same item, in the same slot, in the same session". Where it came from is
    /// worth keeping - but as provenance beside the blob, not inside it.
    /// </para>
    /// </summary>
    public static void Normalise(Item item)
    {
        item.Id = 0;
        item.Mode = ItemMode.Stored;
        item.Location = ItemLocation.None;
        item.Page = 0;
        item.X = 0;
        item.Y = 0;
    }

    /// <summary>
    /// Writes an item to bytes and <b>proves the bytes read back</b>, or refuses with a
    /// reason. Returns the blob and its hash.
    /// <para>
    /// The format allows lists the editor can build but cannot read - six stat ids lead a
    /// run whose followers are written with no ids of their own, and a run written out of
    /// order is an unreadable bitstream. <c>ItemStatList.Ordered</c> keeps them in order on
    /// write, and this is the proof that it did. Crafting once produced 471 items of 41454
    /// that could not be read back, and nothing caught it, because a save that writes and
    /// reads under the same wrong assumption round-trips perfectly.
    /// </para>
    /// <para>
    /// <b>Why prove it here rather than trust the writer.</b> A bad item in a save is one
    /// broken character and the player still has the game. A bad blob in the vault is
    /// permanent, may be the only copy, and will not be noticed for months. At roughly
    /// <b>40 microseconds</b> an item the proof is free, and it turns a silent corruption
    /// into a refused deposit that says why.
    /// </para>
    /// </summary>
    public static bool TryCapture(Item item, int statIdBits,
                                  out byte[] blob, out byte[] hash, out string? refused)
    {
        using var pinned = new Pinned(statIdBits);

        blob = [];
        hash = [];
        refused = null;

        try
        {
            blob = Item.Write(item, Version);
        }
        catch (Exception problem)
        {
            refused = L($"not writable: {problem.Message}");
            return false;
        }

        byte[] again;

        try
        {
            // Read what was written, then write that. Comparing the two writes rather than
            // the two items is the only comparison that needs no notion of item equality,
            // and it is the same test SaveVerifier applies to a whole save.
            again = Item.Write(Item.Read(new D2SLib.IO.BitReader(blob), Version), Version);
        }
        catch (Exception problem)
        {
            refused = L($"unreadable after write: {problem.Message}");
            return false;
        }

        if (!again.AsSpan().SequenceEqual(blob))
        {
            refused = again.Length != blob.Length
                ? L($"changed in write and read: {blob.Length} bytes out, {again.Length} back")
                : L("changed in write and read");
            return false;
        }

        hash = SHA256.HashData(blob);
        return true;
    }

    /// <summary>
    /// Pins the stat-id width for one read or write, and puts back whatever it was.
    /// <para>
    /// <b>A blob is not self-describing.</b> An item's stat list is read with ids
    /// <c>Globals.stat_id_bits</c> wide - 9 in vanilla, 10 in current PD2 - and that is a
    /// process-wide switch every file reader sets for itself. So a blob written at ten and
    /// read at nine decodes into whatever the bits happen to spell: a plain white War Staff
    /// came back carrying "+120% Faster Cast Rate" and "passive_mastery_melee_crit 145", and
    /// the ones that did not decode into anything at all threw and vanished from the page.
    /// </para>
    /// <para>
    /// The width therefore travels with the blob - the vault keeps it in its header - and is
    /// pinned here rather than hoped for. Put back afterwards because the character session
    /// owns that global too, and a vault page must not change how the open save is read.
    /// </para>
    /// </summary>
    private readonly struct Pinned : IDisposable
    {
        private readonly int _was;

        public Pinned(int statIdBits)
        {
            _was = D2SLib.Globals.stat_id_bits;
            D2SLib.Globals.stat_id_bits = statIdBits;
        }

        public void Dispose() => D2SLib.Globals.stat_id_bits = _was;
    }

    /// <summary>
    /// A detached copy of an item, written and read back at the vault's version <b>and</b> the
    /// given stat-id width, both pinned. The write used to take whatever width the process last
    /// set: it worked in the editor only because the page had just set the character's, and a
    /// copy made anywhere else wrote ids the read could not find ("No ItemStatCost record found
    /// for id: 528"), found by the first test of editing a vault item in place.
    /// </summary>
    public static Item Copy(Item item, int statIdBits)
    {
        using var pinned = new Pinned(statIdBits);
        return Item.Read(new D2SLib.IO.BitReader(Item.Write(item, Version)), Version);
    }

    /// <summary>An item as <see cref="Copy"/> writes it: what a check compares, byte for byte, across a change that should move nothing.</summary>
    public static byte[] Bytes(Item item, int statIdBits)
    {
        using var pinned = new Pinned(statIdBits);
        return Item.Write(item, Version).ToArray();
    }

    /// <summary>The item a blob holds. The caller gives it an id and a place.</summary>
    public static Item Restore(byte[] blob, int statIdBits)
    {
        using var pinned = new Pinned(statIdBits);
        return Item.Read(new D2SLib.IO.BitReader(blob), Version);
    }
}
