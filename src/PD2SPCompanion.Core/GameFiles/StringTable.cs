using System.Text;

namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// The game's string tables, which turn a key such as <c>StrSklTabItem3</c> into the text a
/// player actually sees.
/// <para>
/// The excel tables hold keys, not words: <c>skilldesc.txt</c> gives a skill's name as a key
/// and <c>charstats.txt</c> gives the skill tabs the same way. Reading them matters because
/// PD2 renames things - its own <c>patchstring.tbl</c> turns "One Hand Mastery" into "One
/// Handed Weapon Mastery" and "Dopplezon" into "Decoy".
/// </para>
/// </summary>
public sealed class StringTable
{
    /// <summary>
    /// In archive order, so PD2's own table is read last and wins. These are the three the
    /// game itself loads.
    /// </summary>
    private static readonly string[] Tables =
    [
        @"data\local\lng\eng\string.tbl",
        @"data\local\lng\eng\expansionstring.tbl",
        @"data\local\lng\eng\patchstring.tbl",
    ];

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where each key's text is: its position in the table that supplies it.</summary>
    private readonly Dictionary<string, int> _positions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which table supplies each key's text, as the number its strings are counted from.</summary>
    private readonly Dictionary<string, int> _bases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the game numbers each table's strings from: <c>string.tbl</c> from 0,
    /// <c>patchstring.tbl</c> from 10000, <c>expansionstring.tbl</c> from 20000 - so
    /// <c>expansionstring</c>'s 569th string is 20568. See <see cref="IndexOf"/>.
    /// </summary>
    private static readonly int[] Bases = [0, 20000, 10000];

    public static StringTable? Current { get; private set; }

    public int Count => _strings.Count;

    public static StringTable Load(GameData game)
    {
        var table = new StringTable();

        for (int ordinal = 0; ordinal < Tables.Length; ordinal++)
        {
            string path = Tables[ordinal];
            var hit = game.Read(path);
            if (hit is null)
                continue;

            try
            {
                // Within one table the first entry for a key wins. A table can hold the
                // same key twice - PD2's own has StrSklTabItem6 as both "Combat Skills" and
                // "Summoning Skills" - and the game, which hashes the key and takes the
                // first node it matches, uses the first. Assigning as we walk took the last
                // and gave the Paladin's combat tree the Necromancer's name.
                var one = new Dictionary<string, (string Value, int Position)>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value, position) in Parse(hit.Value.Data))
                    one.TryAdd(key, (value, position));

                // Across tables the later one wins, which is how PD2 overrides vanilla.
                foreach (var (key, (value, position)) in one)
                {
                    table._strings[key] = value;
                    table._positions[key] = position;
                    table._bases[key] = Bases[ordinal];
                }
            }
            catch (Exception)
            {
                // A table that will not parse simply contributes nothing; the keys it would
                // have supplied fall back to their raw form.
            }
        }

        Current = table;
        return table;
    }

    /// <summary>The text for a key, or null when the tables do not have it.</summary>
    public string? Get(string? key) =>
        key is { Length: > 0 } && _strings.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : null;

    /// <summary>A key's position in the table that supplies its text, or null when no table has it.</summary>
    public int? PositionOf(string? key) =>
        key is { Length: > 0 } && _positions.TryGetValue(key, out int position) ? position : null;

    /// <summary>
    /// A key's string index as the game numbers them: the supplying table's start - see
    /// <see cref="Bases"/> - plus the key's position in it. What a runeword's id is made of: see
    /// <c>Runewords.IdOf</c>. Null when no table has the key.
    /// </summary>
    public int? IndexOf(string? key) =>
        key is { Length: > 0 } && _positions.TryGetValue(key, out int position) && position >= 0
            ? _bases[key] + position
            : null;

    /// <summary>
    /// A skill tab's name. The tables store these as the item affix that grants them -
    /// "+%d to Bow and Crossbow Skills" - so the phrasing is trimmed back to the tab itself.
    /// </summary>
    public string? Tab(string? key)
    {
        string? text = Get(key);
        if (text is null)
            return null;

        const string prefix = "+%d to ";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].Trim()
            : text.Trim();
    }

    /// <summary>
    /// Walks every node in a .tbl.
    /// <para>
    /// The file is a hash table, but every node is read rather than hashed: the keys are
    /// wanted in bulk, and reproducing the game's hash exactly would be a way to get subtly
    /// wrong answers for no gain.
    /// </para>
    /// <para>
    /// Header: crc, element count and hash size, a version byte, then the offset where the
    /// strings begin. Element indices follow, then one 17-byte node per hash slot.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Key, string Value, int Position)> Parse(byte[] data)
    {
        const int headerLength = 21;
        const int nodeLength = 17;

        if (data.Length < headerLength)
            yield break;

        ushort elements = BitConverter.ToUInt16(data, 2);
        uint slots = BitConverter.ToUInt32(data, 4);

        long nodes = headerLength + elements * 2L;

        // The element list says which hash slot holds the n-th string; a string's position is
        // its place in that list, not its slot.
        var positionOfSlot = new Dictionary<int, int>();
        for (int position = 0; position < elements && headerLength + position * 2 + 1 < data.Length; position++)
            positionOfSlot.TryAdd(BitConverter.ToUInt16(data, headerLength + position * 2), position);

        for (uint i = 0; i < slots; i++)
        {
            long at = nodes + i * (long)nodeLength;
            if (at + nodeLength > data.Length)
                yield break;

            // The first byte says whether the slot is in use.
            if (data[at] == 0)
                continue;

            string key = ReadAscii(data, BitConverter.ToUInt32(data, (int)at + 7));
            if (key.Length == 0)
                continue;

            yield return (key, ReadAscii(data, BitConverter.ToUInt32(data, (int)at + 11)),
                          positionOfSlot.TryGetValue((int)i, out int found) ? found : -1);
        }
    }

    /// <summary>A null-terminated string at an absolute offset.</summary>
    private static string ReadAscii(byte[] data, long at)
    {
        if (at <= 0 || at >= data.Length)
            return "";

        int end = (int)at;
        while (end < data.Length && data[end] != 0)
            end++;

        return Plain(Encoding.ASCII.GetString(data, (int)at, end - (int)at));
    }

    /// <summary>
    /// A string with the game's colour codes taken out.
    /// <para>
    /// D2 colours a string in place: <c>0xFF</c>, then <c>c</c>, then one character saying
    /// which colour - so PD2's Worldstone Shard is stored as the shard's name behind a
    /// three-byte code, and read straight out it shows as <c>??c1Worldstone Shard</c>. The
    /// editor paints its own colours from rarity, so the codes are noise here.
    /// </para>
    /// </summary>
    private static string Plain(string text)
    {
        if (text.IndexOf('c') < 0)
            return text;

        var kept = new StringBuilder(text.Length);

        for (int n = 0; n < text.Length; n++)
        {
            // The 0xFF is not ASCII, so it has already become a '?' by the time it is
            // here - which is why the marker is matched rather than the byte.
            bool code = text[n] == '?' && n + 2 < text.Length
                        && (text[n + 1] == 'c' || (text[n + 1] == '?' && text[n + 2] == 'c'));

            if (!code)
            {
                kept.Append(text[n]);
                continue;
            }

            n += text[n + 1] == 'c' ? 2 : 3;
        }

        return kept.ToString();
    }
}
