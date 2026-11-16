using System.Buffers.Binary;
using System.IO.Compression;

namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// Minimal PNG writer, used to eyeball decoded sprites from the command line. The UI
/// draws pixels directly and does not need this.
/// </summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, int width, int height, uint[] bgra)
    {
        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // truecolour with alpha
        WriteChunk(file, "IHDR", header);

        // Each scanline is prefixed with its filter type, here always 0 (none).
        var raw = new byte[height * (1 + width * 4)];
        int at = 0;
        for (int y = 0; y < height; y++)
        {
            raw[at++] = 0;
            for (int x = 0; x < width; x++)
            {
                uint pixel = bgra[y * width + x];
                raw[at++] = (byte)(pixel >> 16);   // r
                raw[at++] = (byte)(pixel >> 8);    // g
                raw[at++] = (byte)pixel;           // b
                raw[at++] = (byte)(pixel >> 24);   // a
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw);
        WriteChunk(file, "IDAT", compressed.ToArray());

        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var name = new byte[4];
        for (int i = 0; i < 4; i++)
            name[i] = (byte)type[i];
        stream.Write(name);
        stream.Write(data);

        // The CRC covers the chunk type followed by its data.
        uint crc = 0xFFFFFFFF;
        crc = Crc(name, crc);
        crc = Crc(data, crc);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xFFFFFFFF);
        stream.Write(checksum);
    }

    private static uint Crc(byte[] data, uint crc)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
