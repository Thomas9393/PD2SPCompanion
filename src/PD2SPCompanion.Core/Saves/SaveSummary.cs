namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// What a save says about itself, read from the fixed header alone.
/// <para>
/// A full parse needs the game's tables loaded and can fail on a save written by another
/// season. Listing characters must not depend on either, so this reads the handful of
/// fixed-offset fields directly: a file that cannot be opened still appears in the list,
/// and says why when it is chosen.
/// </para>
/// </summary>
public sealed record SaveSummary(
    string Path,
    string FileName,
    string Name,
    byte ClassId,
    string ClassName,
    int Level,
    bool IsHardcore,
    bool IsExpansion,
    bool HasDied,
    DateTime LastSaved,
    DateTime? Created,
    DateTime? LastPlayed,
    long Bytes,
    int Act = 1)
{
    /// <summary>
    /// Whether this character is dead, which only a hardcore character can be.
    /// <para>
    /// The bit behind <see cref="HasDied"/> records a death having happened, not a death
    /// standing: a softcore character carries it from its first death onward and goes on
    /// playing. Read on a live level 23 Amazon with an empty corpse list, so the death was
    /// over and the bit had stayed. Nearly every softcore character past the early levels
    /// has it, which makes it worth nothing as a badge and worse than nothing in red.
    /// </para>
    /// </summary>
    public bool IsDead => HasDied && IsHardcore;

    // Fixed offsets in the v96 header. The whole header is 335 bytes, so anything shorter
    // is not a character file.
    private const int SignatureOffset = 0x00;
    private const int NameOffset = 0x14;
    private const int StatusOffset = 0x24;
    private const int ClassOffset = 0x28;
    private const int LevelOffset = 0x2B;

    // Unix timestamps. There is no play-time duration anywhere in the format; these are the
    // nearest thing it records.
    private const int CreatedOffset = 0x2C;
    private const int LastPlayedOffset = 0x30;
    private const int DifficultyOffset = 0xA8;
    private const int MinimumHeader = 0xAB;

    private const uint Signature = 0xAA55AA55;

    /// <summary>Just what the character is; the flags are shown separately as badges.</summary>
    public string Description => L($"Level {Level} {ClassName}");

    /// <summary>How the game labels the ruleset: hardcore or softcore.</summary>
    public string Mode => IsHardcore ? "HC" : "SC";

    /// <summary>Classic characters cannot use expansion content, so it is worth showing.</summary>
    public string Ruleset => IsExpansion ? L("EXPANSION") : L("CLASSIC");

    /// <summary>
    /// Header and nothing else: no quests, attributes or items.
    /// <para>
    /// Creating a character in the game drops you straight into it, so a played character is
    /// never this. A stub is a file left behind by something that did not finish, not a
    /// character anyone can open.
    /// </para>
    /// </summary>
    public bool IsStub => Bytes <= HeaderOnlyLength;

    /// <summary>The fixed header, which is the whole of such a file.</summary>
    public const int HeaderOnlyLength = 335;

    /// <summary>Null when the file is not a readable v96 character.</summary>
    public static SaveSummary? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            byte[] header = new byte[MinimumHeader];

            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(header, 0, header.Length) < header.Length)
                    return null;
            }

            if (BitConverter.ToUInt32(header, SignatureOffset) != Signature)
                return null;

            // The name is a fixed 16-byte field padded with nulls.
            string name = System.Text.Encoding.ASCII
                .GetString(header, NameOffset, 16)
                .TrimEnd('\0');
            if (name.Length == 0)
                name = System.IO.Path.GetFileNameWithoutExtension(path);

            byte status = header[StatusOffset];
            byte classId = header[ClassOffset];

            return new SaveSummary(
                Path: path,
                FileName: info.Name,
                Name: name,
                ClassId: classId,
                ClassName: CharacterClasses.NameOf(classId) ?? L("Unknown"),
                Level: header[LevelOffset],
                IsHardcore: (status & 0x04) != 0,
                IsExpansion: (status & 0x20) != 0,
                HasDied: (status & 0x08) != 0,
                LastSaved: info.LastWriteTime,
                Created: Timestamp(header, CreatedOffset),
                LastPlayed: Timestamp(header, LastPlayedOffset),
                Bytes: info.Length,
                Act: ActOf(header));
        }
        catch (Exception)
        {
            // A file being written by the game, or one we cannot read, simply is not listed.
            return null;
        }
    }

    /// <summary>
    /// The act the character is in, 1 to 5: the header keeps a byte for each difficulty - its high bit set on
    /// the one last played, its low three bits the act reached there, from nought (D2S.Location). The first
    /// act where none is marked: a character not yet played.
    /// </summary>
    private static int ActOf(byte[] header)
    {
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            byte place = header[DifficultyOffset + difficulty];
            if ((place & 0x80) != 0)
                return Math.Clamp((place & 0x07) + 1, 1, 5);
        }
        return 1;
    }

    /// <summary>
    /// A Unix timestamp from the header, or null when it is not a plausible date - the
    /// field is zero on some saves and there is no point showing 1970.
    /// </summary>
    private static DateTime? Timestamp(byte[] header, int offset)
    {
        uint seconds = BitConverter.ToUInt32(header, offset);
        if (seconds is < 946_684_800 or > 2_140_000_000)   // the year 2000 .. ~2037
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
    }

    /// <summary>
    /// Every character in a folder, most recently played first. The header's own timestamp
    /// is preferred over the file's write time, which anything touching the file can move.
    /// </summary>
    public static IReadOnlyList<SaveSummary> FromFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetFiles(folder, "*.d2s")
            .Select(TryRead)
            .OfType<SaveSummary>()
            .Where(save => !save.IsStub)
            .OrderByDescending(save => save.LastPlayed ?? save.LastSaved)
            .ToList();
    }
}
