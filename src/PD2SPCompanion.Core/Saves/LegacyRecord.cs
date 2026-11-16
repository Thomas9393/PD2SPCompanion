using System.Text.Json;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// The legacy items whose stamp no longer says so: an item of a season whose encoding the game reads no more, written
/// again by the encoding repair in this season's and stamped as this season stamps - the only stamp
/// the game would read it by. Kept here, by its id and base, with the season it is of, so it stays a legacy item
/// (<see cref="ItemSeason"/>). One file in the editor's data, written only by a repair;
/// an item edited since is taken off it (<see cref="ItemSeason.MakeCurrent"/>).
/// </summary>
public static class LegacyRecord
{
    /// <summary>One item, as the record keeps it.</summary>
    public sealed record Entry(uint Id, string Code, string Of);

    private static string FilePath => Path.Combine(Storage.EditorData.Root, "legacy-items.json");

    private static readonly object Gate = new();

    /// <summary>What the file holds, read once, by key; null until first asked.</summary>
    private static Dictionary<string, Entry>? _entries;

    /// <summary>The file the record was read from: another data folder (a test's) reads its own.</summary>
    private static string? _readFrom;

    private static string Key(uint id, string code) => $"{id:X8}:{code.Trim()}";

    private static Dictionary<string, Entry> Entries()
    {
        string path = FilePath;
        if (_entries is not null && _readFrom == path)
            return _entries;

        var read = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                foreach (var entry in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [])
                    read[Key(entry.Id, entry.Code)] = entry;
            }
        }
        catch (Exception problem)
        {
            // A record that will not read leaves those items this season's: they are checked, never lost. The file
            // is the only copy of those marks, so it goes aside before a later save can write over it.
            Log.Error("season", $"Legacy record {path} not read", problem);
            try
            {
                File.Move(path, path + ".bad", overwrite: true);
            }
            catch (Exception moving)
            {
                Log.Caught(moving);
            }
        }

        _readFrom = path;
        return _entries = read;
    }

    /// <summary>The season the record says the item is of, or null - for nearly every item.</summary>
    public static string? SeasonOf(Item item)
    {
        if (item.IsCompact)
            return null;

        lock (Gate)
        {
            var entries = Entries();
            return entries.Count > 0 && entries.TryGetValue(Key(item.Id, item.Code), out var entry) ? entry.Of : null;
        }
    }

    /// <summary>Items written again by a repair, each with the season it is of.</summary>
    public static void Add(IEnumerable<(Item Item, string Of)> items)
    {
        lock (Gate)
        {
            var entries = Entries();
            int before = entries.Count;
            foreach (var (item, of) in items.Where(each => !each.Item.IsCompact))
                entries[Key(item.Id, item.Code)] = new Entry(item.Id, item.Code.Trim(), of);
            if (entries.Count != before || before == 0)
                Save(entries);
        }
    }

    /// <summary>An item edited, this season's from now on.</summary>
    public static void Forget(Item item)
    {
        lock (Gate)
        {
            var entries = Entries();
            if (!entries.Remove(Key(item.Id, item.Code)))
                return;

            // Reached at the start of an edit, which goes on: a locked data folder is no reason to stop it.
            try
            {
                Save(entries);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
            }
        }
    }

    private static void Save(Dictionary<string, Entry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        SaveWriter.WriteSafely(FilePath, JsonSerializer.SerializeToUtf8Bytes(entries.Values.OrderBy(entry => entry.Of).ThenBy(entry => entry.Id).ToList(),
                                                                             new JsonSerializerOptions { WriteIndented = true }));
    }
}
