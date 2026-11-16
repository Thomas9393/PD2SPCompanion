using System.Collections;
using D2SLib.IO;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The save's bit writer and reader, written straight from and into values,
/// against the byte arrays they once went through: the same bits for every width, signed or not, short of the type
/// and past it, and the same values read back.
/// </summary>
public sealed class BitIoTests
{
    /// <summary>The bits the old writer wrote: the value's bytes, padded or cut to the width, lowest bit first.</summary>
    private static bool[] OldBits(byte[] bytes, int width)
    {
        Array.Resize(ref bytes, (width - 1) / 8 + 1);
        var bits = new BitArray(bytes);
        return [.. Enumerable.Range(0, width).Select(i => bits[i])];
    }

    private static bool[] Written(Action<BitWriter> write, out int length)
    {
        var writer = new BitWriter();
        write(writer);
        length = writer.Length;
        var bytes = writer.ToArray();
        var all = new BitArray(bytes);
        return [.. Enumerable.Range(0, length).Select(i => all[i])];
    }

    [Fact]
    public void Every_width_writes_the_bits_the_byte_arrays_wrote()
    {
        var random = new Random(14);
        for (int round = 0; round < 400; round++)
        {
            uint u = (uint)random.NextInt64(0, uint.MaxValue + 1L);
            int s = random.Next(int.MinValue, int.MaxValue);
            int width = random.Next(1, 41);

            Assert.Equal(OldBits(BitConverter.GetBytes(u), width), Written(w => w.WriteUInt32(u, width), out _));
            Assert.Equal(OldBits(BitConverter.GetBytes(s), width), Written(w => w.WriteInt32(s, width), out _));
            Assert.Equal(OldBits(BitConverter.GetBytes((ushort)u), width), Written(w => w.WriteUInt16((ushort)u, width), out _));
            Assert.Equal(OldBits([(byte)u], width), Written(w => w.WriteByte((byte)u, width), out _));

            var bytes = new byte[random.Next(1, 6)];
            random.NextBytes(bytes);
            int byteWidth = random.Next(1, bytes.Length * 8 + 17);
            Assert.Equal(OldBits([.. bytes], byteWidth), Written(w => w.WriteBytes(bytes, byteWidth), out _));
        }
    }

    [Fact]
    public void What_is_written_reads_back_as_the_byte_arrays_read_it()
    {
        var random = new Random(41);
        for (int round = 0; round < 400; round++)
        {
            var bytes = new byte[12];
            random.NextBytes(bytes);
            int width = random.Next(1, 41);

            // The old reader: the bits to bytes, cut to the type's width.
            byte[] OldRead(int bits)
            {
                var all = new BitArray(bytes);
                var read = new byte[(bits - 1) / 8 + 1];
                for (int i = 0; i < bits; i++)
                    if (all[i])
                        read[i / 8] |= (byte)(1 << (i % 8));
                return read;
            }

            byte[] Cut(byte[] read, int to) { Array.Resize(ref read, to); return read; }

            Assert.Equal(BitConverter.ToUInt32(Cut(OldRead(width), 4)), new BitReader(bytes).ReadUInt32(width));
            Assert.Equal(BitConverter.ToInt32(Cut(OldRead(width), 4)), new BitReader(bytes).ReadInt32(width));
            Assert.Equal(BitConverter.ToUInt16(Cut(OldRead(width), 2)), new BitReader(bytes).ReadUInt16(width));
            Assert.Equal(Cut(OldRead(width), 1)[0], new BitReader(bytes).ReadByte(width));
            Assert.Equal(OldRead(width), new BitReader(bytes).ReadBits(width));

            var reader = new BitReader(bytes);
            reader.ReadUInt32(width);
            Assert.Equal(width, reader.Position);
        }
    }

    [Fact]
    public void A_bit_written_again_replaces_it_and_a_skip_writes_nothing()
    {
        // A seek back and a write over: the old BitArray set each bit, zeros included.
        var writer = new BitWriter();
        writer.WriteByte(0xFF);
        writer.SeekBits(2);
        writer.WriteByte(0, 3);
        writer.SeekBits(8);
        writer.SkipBits(5);
        writer.WriteBit(true);
        Assert.Equal(new byte[] { 0b1110_0011, 0b0010_0000 }, writer.ToArray());
        Assert.Equal(14, writer.Length);

        // Nothing written: one empty byte, as before.
        Assert.Equal(new byte[] { 0 }, new BitWriter().ToArray());
    }

    [Fact]
    public void Reading_past_the_end_throws_and_stays_where_it_was()
    {
        var reader = new BitReader([0xFF]);
        reader.ReadByte(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadUInt32(8));
        Assert.Equal(4, reader.Position);
    }
}
