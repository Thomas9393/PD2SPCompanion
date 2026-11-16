using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace D2SLib.IO
{
    /// <summary>
    /// Bits written lowest first into bytes, as the file holds them. A byte buffer, written up to a byte at a step:
    /// it was a BitArray written a bit at a time, every field through two to four arrays of its own, and the bit
    /// writing was most of a save's own cost. A bit written again over one
    /// already written - a seek back - replaces it, ones and zeros alike.
    /// </summary>
    public class BitWriter : IDisposable
    {
        byte[] _buffer;

        private int _position = 0;
        public int Position {
            get {
                return _position;
            }
            private set {
                if (value > Length) {
                    Length = value;
                }
                _position = value;
            }
        }
        public Int32 Length { get; private set; }
        public BitWriter(int initialCapacity)
        {
            _buffer = new byte[Math.Max(1, (initialCapacity + 7) / 8)];
            Position = 0;
        }

        public BitWriter() : this(1024)
        {
        }

        /// <summary>Room for the bits up to <paramref name="end"/>, doubling as the BitArray did.</summary>
        private void Room(int end)
        {
            int bytes = (end + 7) >> 3;
            if (bytes <= _buffer.Length)
                return;

            int grown = _buffer.Length;
            while (grown < bytes)
                grown *= 2;
            Array.Resize(ref _buffer, grown);
        }

        public void WriteBit(bool value)
        {
            Room(Position + 1);
            int at = Position;
            byte mask = (byte)(1 << (at & 7));
            if (value)
                _buffer[at >> 3] |= mask;
            else
                _buffer[at >> 3] &= (byte)~mask;
            Position = at + 1;
        }

        /// <summary>The value's lowest <paramref name="numberOfBits"/> bits, lowest first; zeros past its 64.</summary>
        private void WriteValue(ulong value, int numberOfBits)
        {
            if (numberOfBits <= 0)
                return;

            Room(Position + numberOfBits);
            int at = Position;
            int left = numberOfBits;
            while (left > 0)
            {
                int offset = at & 7;
                int step = Math.Min(8 - offset, left);
                int mask = ((1 << step) - 1) << offset;
                int bits = (int)(value & (ulong)((1 << step) - 1)) << offset;
                ref byte b = ref _buffer[at >> 3];
                b = (byte)((b & ~mask) | bits);
                value = step >= 64 ? 0 : value >> step;
                at += step;
                left -= step;
            }
            Position = at;
        }

        public void WriteBits(BitArray bits, int numberOfBits)
        {
            for (int i = 0; i < numberOfBits; i++)
            {
                WriteBit(bits[i]);
            }
        }

        // The fields are written straight from their value, lowest bit first, and every bit past the value's own
        // width a zero - as the bytes padded out to the width asked always gave them, with no sign carried up.

        public void WriteBytes(byte[] value, int numberOfBits)
        {
            int whole = 0;
            while (numberOfBits - whole >= 8)
            {
                int at = whole >> 3;
                WriteValue(at < value.Length ? value[at] : (byte)0, 8);
                whole += 8;
            }
            if (numberOfBits > whole)
            {
                int at = whole >> 3;
                WriteValue(at < value.Length ? value[at] : (byte)0, numberOfBits - whole);
            }
        }
        public void WriteBytes(byte[] value)
        {
            WriteBytes(value, value.Length * 8);
        }

        public void WriteByte(byte value, int size)
        {
            WriteValue(value, size);
        }

        public void WriteByte(byte value)
        {
            WriteValue(value, 8);
        }

        public void WriteUInt16(UInt16 value, int numberOfBits)
        {
            WriteValue(value, numberOfBits);
        }

        public void WriteUInt16(UInt16 value)
        {
            WriteValue(value, 16);
        }
        public void WriteUInt32(UInt32 value, int numberOfBits)
        {
            WriteValue(value, numberOfBits);
        }
        public void WriteUInt32(UInt32 value)
        {
            WriteValue(value, 32);
        }
        public void WriteInt32(Int32 value, int numberOfBits)
        {
            WriteValue(unchecked((uint)value), numberOfBits);
        }
        public void WriteInt32(Int32 value)
        {
            WriteValue(unchecked((uint)value), 32);
        }
        public void WriteString(string s, int length)
        {
            WriteBytes(System.Text.Encoding.ASCII.GetBytes(s), length * 8);
        }

        /// <summary>
        /// The bytes written, to the last bit reached. Bits past it are zeros: nothing is written beyond the furthest
        /// point, and a skip forward writes nothing.
        /// </summary>
        public byte[] ToArray()
        {
            byte[] bytes = new byte[((Length - 1) / 8) + 1];
            Array.Copy(_buffer, bytes, Math.Min(bytes.Length, _buffer.Length));

            // The last byte's bits past the length, as the old copy bit by bit left them: none.
            int spare = bytes.Length * 8 - Length;
            if (spare > 0 && spare < 8 && Length > 0)
                bytes[^1] &= (byte)(0xFF >> spare);
            return bytes;
        }
        public void SkipBits(int numberOfBits)
        {
            Position += numberOfBits;
            Room(Position);
        }
        public void Skip(int bytes)
        {
            SkipBits(bytes * 8);
        }
        public void SeekBits(int bitPosition)
        {
            Position = bitPosition;
            Room(Position);
        }
        public void Seek(int bytePostion)
        {
            SeekBits(bytePostion * 8);
        }
        public void Align()
        {
            Position = (Position + 7) & ~7;
            Room(Position);
        }
        public void Dispose()
        {
            _buffer = null;
        }
    }

}
