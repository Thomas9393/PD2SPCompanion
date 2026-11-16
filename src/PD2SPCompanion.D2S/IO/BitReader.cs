using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace D2SLib.IO
{
    public class BitReader : IDisposable
    {
        // Read from the bytes themselves, a bit at its place - byte at/8, lowest bit first, as a BitArray of them
        // numbered it - and a field straight into its value: each made a byte array and resized it before, on every
        // property of every item read. Past the end throws as the BitArray did.
        byte[] _bytes;
        readonly int _length;
        public int Position { get; private set; }

        public BitReader(byte[] bytes)
        {
            Position = 0;
            _bytes = bytes;
            _length = bytes.Length * 8;
        }

        private bool Bit(int at)
        {
            if ((uint)at >= (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(at), at, "Past the end of the bits read.");
            return ((_bytes[at >> 3] >> (at & 7)) & 1) != 0;
        }

        public bool ReadBit()
        {
            return Bit(Position++);
        }

        /// <summary>The next <paramref name="numberOfBits"/> bits as a value, lowest first: those past its 64 read and dropped.</summary>
        private ulong ReadValue(int numberOfBits)
        {
            if (numberOfBits <= 0)
                return 0;

            // The whole of it there, or nothing read: a read past the end throws where it stood.
            if ((uint)(Position + numberOfBits - 1) >= (uint)_length)
                Bit(Position + numberOfBits - 1);

            // Up to a byte at a step.
            ulong value = 0;
            int at = Position, got = 0;
            while (got < numberOfBits)
            {
                int offset = at & 7;
                int step = Math.Min(8 - offset, numberOfBits - got);
                ulong bits = (ulong)((_bytes[at >> 3] >> offset) & ((1 << step) - 1));
                if (got < 64)
                    value |= bits << got;
                at += step;
                got += step;
            }
            Position = at;
            return value;
        }

        public byte[] ReadBits(int numberOfBits)
        {
            byte[] bytes = new byte[(numberOfBits-1)/8+1];
            for(int i = 0; i < numberOfBits; i++)
            {
                if(Bit(Position + i))
                {
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            Position += numberOfBits;
            return bytes;
        }

        public byte[] ReadBytes(int numberOfBytes)
        {
            return ReadBits(numberOfBytes * 8);
        }

        // The low bits of what was read, as the bytes cut to the type's width gave them; no sign carried down.
        public byte ReadByte(int bits)
        {
            return unchecked((byte)ReadValue(bits));
        }

        public byte ReadByte()
        {
            return unchecked((byte)ReadValue(8));
        }

        public UInt16 ReadUInt16(int bits)
        {
            return unchecked((UInt16)ReadValue(bits));
        }

        public UInt16 ReadUInt16()
        {
            return unchecked((UInt16)ReadValue(16));
        }

        public UInt32 ReadUInt32(int bits)
        {
            return unchecked((UInt32)ReadValue(bits));
        }

        public UInt32 ReadUInt32()
        {
            return unchecked((UInt32)ReadValue(32));
        }

        public Int32 ReadInt32(int bits)
        {
            return unchecked((Int32)(UInt32)ReadValue(bits));
        }

        public Int32 ReadInt32()
        {
            return unchecked((Int32)(UInt32)ReadValue(32));
        }

        public string ReadString(int bytes)
        {
            return System.Text.Encoding.ASCII.GetString(ReadBytes(bytes)).Trim('\0');
        }

        public void SeekBits(int bitPosition)
        {
            Position = bitPosition;
        }
        public void Seek(int bytePostion)
        {
            SeekBits(bytePostion * 8);
        }

        public void Align()
        {
            Position = (Position + 7) & ~7;
        }

        public void Dispose()
        {
            _bytes = null;
        }
    }
}
