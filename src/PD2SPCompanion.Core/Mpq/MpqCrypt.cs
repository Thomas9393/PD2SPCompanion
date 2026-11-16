namespace PD2SPCompanion.Core.Mpq;

/// <summary>
/// MoPaQ hashing and block encryption. Straight port of the algorithm Blizzard's
/// Storm.dll uses; the crypt table is generated once from a fixed seed.
/// </summary>
internal static class MpqCrypt
{
    private static readonly uint[] Table = BuildTable();

    // Hash types, as passed to HashString.
    public const int HashTableOffset = 0;
    public const int HashNameA = 1;
    public const int HashNameB = 2;
    public const int HashFileKey = 3;

    private static uint[] BuildTable()
    {
        var table = new uint[0x500];
        uint seed = 0x00100001;
        for (int i = 0; i < 0x100; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                seed = (seed * 125 + 3) % 0x2AAAAB;
                uint hi = (seed & 0xFFFF) << 16;
                seed = (seed * 125 + 3) % 0x2AAAAB;
                uint lo = seed & 0xFFFF;
                table[i + j * 0x100] = hi | lo;
            }
        }
        return table;
    }

    public static uint HashString(string value, int hashType)
    {
        uint seed1 = 0x7FED7FED;
        uint seed2 = 0xEEEEEEEE;
        foreach (char raw in value)
        {
            // MPQ names are upper-cased and always use backslash separators.
            uint c = char.ToUpperInvariant(raw);
            seed1 = Table[(hashType * 0x100) + c] ^ (seed1 + seed2);
            seed2 = c + seed1 + seed2 + (seed2 << 5) + 3;
        }
        return seed1;
    }

    public static void DecryptBlock(Span<byte> data, uint key)
    {
        uint seed = 0xEEEEEEEE;
        int words = data.Length / 4;
        for (int i = 0; i < words; i++)
        {
            seed += Table[0x400 + (key & 0xFF)];
            uint value = BitConverter.ToUInt32(data.Slice(i * 4, 4));
            value ^= key + seed;
            BitConverter.TryWriteBytes(data.Slice(i * 4, 4), value);
            key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
            seed = value + seed + (seed << 5) + 3;
        }
    }
}
