using System.Buffers.Binary;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// The statistics PD2 keeps for a character - what its <c>.kills</c>, <c>.deaths</c> and
/// <c>.played</c> chat commands print.
/// <para>
/// They live in the <c>st</c> section of the <c>.d2s</c> trailer: 64 bytes, laid out relative to
/// the tag. The shared stash carries the same block at its offset <c>0x10</c> as an account-wide
/// running total. Every field was proven against the commands themselves - timed
/// sessions for the clock, an exact kill count, one kill of each act boss, one death, and a
/// numbered write-and-look for the rest.
/// </para>
/// </summary>
public sealed record PlayerStats(
    uint SecondsPlayed,
    uint MonsterKills,
    int Andariel, int Duriel, int Mephisto, int Diablo, int Baal,
    int Deaths,
    int DiabloClone, int DiabloCloneTier2,
    int Rathma, int RathmaTier2,
    int UberTristram, int UberAncients,
    int MapBosses, int DungeonBosses,
    int PlayerKills)
{
    /// <summary>The block's length, tag included. The next tag, <c>ht</c>, follows it.</summary>
    public const int Length = 0x40;

    /// <summary>
    /// The block in a character's trailer, or null when the save has none - a vanilla save,
    /// or one from before PD2 kept statistics.
    /// <para>
    /// Found by its shape, not by the first <c>st</c>: two bytes that happen to spell it can sit
    /// anywhere in a trailer, but only the real tag has <c>ht</c> exactly one block later.
    /// </para>
    /// </summary>
    public static PlayerStats? FromTrailer(byte[] trailer)
    {
        for (int at = 0; at + Length + 2 <= trailer.Length; at++)
        {
            if (trailer[at] == (byte)'s' && trailer[at + 1] == (byte)'t'
                && trailer[at + Length] == (byte)'h' && trailer[at + Length + 1] == (byte)'t')
            {
                return Read(trailer.AsSpan(at, Length));
            }
        }

        return null;
    }

    /// <summary>Reads a block that starts at its <c>st</c> tag.</summary>
    public static PlayerStats Read(ReadOnlySpan<byte> block)
    {
        return new PlayerStats(
            SecondsPlayed: U32(block, 0x02),
            MonsterKills: U32(block, 0x06),
            Andariel: U16(block, 0x1A), Duriel: U16(block, 0x1C), Mephisto: U16(block, 0x1E),
            Diablo: U16(block, 0x20), Baal: U16(block, 0x22),
            Deaths: U16(block, 0x24),
            DiabloClone: U16(block, 0x0A), DiabloCloneTier2: U16(block, 0x0C),
            Rathma: U16(block, 0x0E), RathmaTier2: U16(block, 0x10),
            UberTristram: U16(block, 0x16), UberAncients: U16(block, 0x18),
            MapBosses: U16(block, 0x26), DungeonBosses: U16(block, 0x28),
            PlayerKills: U16(block, 0x2A));
    }

    private static uint U32(ReadOnlySpan<byte> block, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(block[at..]);

    private static int U16(ReadOnlySpan<byte> block, int at) =>
        BinaryPrimitives.ReadUInt16LittleEndian(block[at..]);

    /// <summary>Play time the way <c>.played</c> prints it: <c>10h 28m 55s</c>.</summary>
    public string Played =>
        SecondsPlayed >= 3600
            ? $"{SecondsPlayed / 3600}h {SecondsPlayed % 3600 / 60}m {SecondsPlayed % 60}s"
            : $"{SecondsPlayed / 60}m {SecondsPlayed % 60}s";
}
