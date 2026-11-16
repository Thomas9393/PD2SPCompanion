using War3Net.IO.Compression;

namespace PD2SPCompanion.Core.Mpq;

/// <summary>
/// Read-only reader for MoPaQ v1 archives, which is what every Diablo II and
/// Project Diablo 2 archive uses. Only the pieces needed to pull files out by
/// name are implemented: enumeration is not required, because the excel table
/// paths are known up front.
/// </summary>
public sealed class MpqArchive : IDisposable
{
    private const uint FlagImplode = 0x00000100;
    private const uint FlagCompress = 0x00000200;
    private const uint FlagEncrypted = 0x00010000;
    private const uint FlagFixKey = 0x00020000;
    private const uint FlagSingleUnit = 0x01000000;
    private const uint FlagSectorCrc = 0x04000000;
    private const uint FlagExists = 0x80000000;

    private const uint HashEntryEmpty = 0xFFFFFFFF;
    private const uint HashEntryDeleted = 0xFFFFFFFE;

    private readonly FileStream _stream;
    private readonly byte[] _hashTable;
    private readonly byte[] _blockTable;
    private readonly uint _hashTableEntries;
    private readonly uint _blockTableEntries;
    private readonly long _archiveOffset;
    private readonly int _sectorSize;

    public string Path { get; }

    private MpqArchive(string path, FileStream stream, long archiveOffset, int sectorSize,
                       byte[] hashTable, byte[] blockTable, uint hashEntries, uint blockEntries)
    {
        Path = path;
        _stream = stream;
        _archiveOffset = archiveOffset;
        _sectorSize = sectorSize;
        _hashTable = hashTable;
        _blockTable = blockTable;
        _hashTableEntries = hashEntries;
        _blockTableEntries = blockEntries;
    }

    public static MpqArchive Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            long archiveOffset = FindHeader(stream);
            stream.Position = archiveOffset;

            var header = new byte[32];
            stream.ReadExactly(header);

            ushort formatVersion = BitConverter.ToUInt16(header, 0x0C);
            if (formatVersion != 0)
                throw new NotSupportedException($"{path}: MPQ format version {formatVersion + 1} is not supported (expected v1).");

            ushort sectorShift = BitConverter.ToUInt16(header, 0x0E);
            uint hashTablePos = BitConverter.ToUInt32(header, 0x10);
            uint blockTablePos = BitConverter.ToUInt32(header, 0x14);
            uint hashEntries = BitConverter.ToUInt32(header, 0x18);
            uint blockEntries = BitConverter.ToUInt32(header, 0x1C);

            var hashTable = ReadTable(stream, archiveOffset + hashTablePos, hashEntries, "(hash table)");
            var blockTable = ReadTable(stream, archiveOffset + blockTablePos, blockEntries, "(block table)");

            return new MpqArchive(path, stream, archiveOffset, 512 << sectorShift,
                                  hashTable, blockTable, hashEntries, blockEntries);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>An MPQ can be appended to another file, so the header is searched on 512-byte boundaries.</summary>
    private static long FindHeader(FileStream stream)
    {
        var probe = new byte[4];
        for (long offset = 0; offset + 32 < stream.Length; offset += 512)
        {
            stream.Position = offset;
            stream.ReadExactly(probe);
            if (probe[0] == (byte)'M' && probe[1] == (byte)'P' && probe[2] == (byte)'Q' && probe[3] == 0x1A)
                return offset;
        }
        throw new InvalidDataException($"{stream.Name}: no MPQ header found.");
    }

    private static byte[] ReadTable(FileStream stream, long position, uint entries, string key)
    {
        var buffer = new byte[entries * 16];
        stream.Position = position;
        stream.ReadExactly(buffer);
        MpqCrypt.DecryptBlock(buffer, MpqCrypt.HashString(key, MpqCrypt.HashFileKey));
        return buffer;
    }

    public bool Contains(string fileName) => TryFindBlockIndex(fileName, out _);

    private bool TryFindBlockIndex(string fileName, out uint blockIndex)
    {
        blockIndex = 0;
        uint start = MpqCrypt.HashString(fileName, MpqCrypt.HashTableOffset) % _hashTableEntries;
        uint nameA = MpqCrypt.HashString(fileName, MpqCrypt.HashNameA);
        uint nameB = MpqCrypt.HashString(fileName, MpqCrypt.HashNameB);

        for (uint i = 0; i < _hashTableEntries; i++)
        {
            int offset = (int)(((start + i) % _hashTableEntries) * 16);
            uint entryBlock = BitConverter.ToUInt32(_hashTable, offset + 12);
            if (entryBlock == HashEntryEmpty)
                return false;
            if (entryBlock == HashEntryDeleted)
                continue;
            if (BitConverter.ToUInt32(_hashTable, offset) == nameA &&
                BitConverter.ToUInt32(_hashTable, offset + 4) == nameB)
            {
                if (entryBlock >= _blockTableEntries)
                    return false;
                blockIndex = entryBlock;
                return true;
            }
        }
        return false;
    }

    public byte[]? ReadFile(string fileName)
    {
        if (!TryFindBlockIndex(fileName, out uint blockIndex))
            return null;

        try
        {
            // One stream, one position: the figure loader reads on a worker while the pages read on
            // the UI thread, so a read is whole before the next one moves the position.
            lock (_stream)
                return ReadBlock(fileName, blockIndex);
        }
        catch (Exception problem) when (problem is OverflowException or ArgumentException or EndOfStreamException or IndexOutOfRangeException)
        {
            // Said with the archive, the block and its flags: a message alone named none of them.
            int at = (int)(blockIndex * 16);
            throw new InvalidDataException(
                $"{System.IO.Path.GetFileName(Path)}: {fileName} (block {blockIndex}, flags 0x{BitConverter.ToUInt32(_blockTable, at + 12):X8}, "
                + $"{BitConverter.ToUInt32(_blockTable, at + 4)} stored, {BitConverter.ToUInt32(_blockTable, at + 8)} bytes) could not be read: {problem.Message}",
                problem);
        }
    }

    private byte[]? ReadBlock(string fileName, uint blockIndex)
    {

        int entry = (int)(blockIndex * 16);
        uint blockOffset = BitConverter.ToUInt32(_blockTable, entry);
        uint compressedSize = BitConverter.ToUInt32(_blockTable, entry + 4);
        uint fileSize = BitConverter.ToUInt32(_blockTable, entry + 8);
        uint flags = BitConverter.ToUInt32(_blockTable, entry + 12);

        if ((flags & FlagExists) == 0)
            return null;

        long dataStart = _archiveOffset + blockOffset;
        uint key = 0;
        if ((flags & FlagEncrypted) != 0)
        {
            // The key comes from the base name only, not the full path.
            int slash = fileName.LastIndexOf('\\');
            string baseName = slash >= 0 ? fileName[(slash + 1)..] : fileName;
            key = MpqCrypt.HashString(baseName, MpqCrypt.HashFileKey);
            if ((flags & FlagFixKey) != 0)
                key = (key + blockOffset) ^ fileSize;
        }

        bool compressed = (flags & (FlagImplode | FlagCompress)) != 0;

        if ((flags & FlagSingleUnit) != 0)
        {
            var raw = ReadRaw(dataStart, (int)compressedSize);
            if ((flags & FlagEncrypted) != 0)
                MpqCrypt.DecryptBlock(raw, key);
            return !compressed || compressedSize == fileSize
                ? raw
                : Decompress(raw, (int)fileSize, flags);
        }

        int sectorCount = (int)((fileSize + _sectorSize - 1) / _sectorSize);

        // A file stored as it is has no sector table: its sectors follow one another from the start.
        // The tables are all compressed, so this first showed in d2exp.mpq's character art (the
        // Amazon's spear, AMRHAM3TN2HT.dcc, read its own pixels as sector offsets).
        if (!compressed)
        {
            var whole = ReadRaw(dataStart, (int)fileSize);
            if ((flags & FlagEncrypted) != 0)
                for (int i = 0; i < sectorCount; i++)
                {
                    int from = i * _sectorSize;
                    MpqCrypt.DecryptBlock(whole.AsSpan(from, Math.Min(_sectorSize, whole.Length - from)), (uint)(key + i));
                }
            return whole;
        }

        int tableEntries = sectorCount + 1 + ((flags & FlagSectorCrc) != 0 ? 1 : 0);
        var sectorTable = ReadRaw(dataStart, tableEntries * 4);
        if ((flags & FlagEncrypted) != 0)
            MpqCrypt.DecryptBlock(sectorTable, key - 1);

        var output = new byte[fileSize];
        int written = 0;
        for (int i = 0; i < sectorCount; i++)
        {
            uint from = BitConverter.ToUInt32(sectorTable, i * 4);
            uint to = BitConverter.ToUInt32(sectorTable, (i + 1) * 4);
            int stored = (int)(to - from);
            int expected = Math.Min(_sectorSize, (int)fileSize - written);

            var sector = ReadRaw(dataStart + from, stored);
            if ((flags & FlagEncrypted) != 0)
                MpqCrypt.DecryptBlock(sector, (uint)(key + i));

            // A sector that did not shrink is stored verbatim.
            var plain = (!compressed || stored == expected)
                ? sector
                : Decompress(sector, expected, flags);

            plain.AsSpan(0, expected).CopyTo(output.AsSpan(written));
            written += expected;
        }
        return output;
    }

    private byte[] ReadRaw(long position, int length)
    {
        var buffer = new byte[length];
        _stream.Position = position;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private static byte[] Decompress(byte[] data, int expectedSize, uint flags)
    {
        // IMPLODE predates the multi-compression byte: the payload is raw PKWARE DCL.
        if ((flags & FlagImplode) != 0 && (flags & FlagCompress) == 0)
            return PKLibCompression.Decompress(data, (uint)expectedSize);

        byte mask = data[0];
        var payload = new MemoryStream(data, 1, data.Length - 1);
        return mask switch
        {
            0x08 => PKLibCompression.Decompress(payload, (uint)expectedSize),
            0x02 => ZLibCompression.Decompress(payload, (uint)expectedSize, true),
            0x10 => BZip2Compression.Decompress(payload, (uint)expectedSize),
            _ => throw new NotSupportedException($"Unsupported MPQ compression mask 0x{mask:X2}.")
        };
    }

    public void Dispose() => _stream.Dispose();
}
