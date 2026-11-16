using PD2SPCompanion.Core.Mpq;

namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// The stack of MPQ archives a Project Diablo 2 install loads, in override order.
/// A file is resolved from the first archive that has it, which is how the game
/// itself layers mod data over the base game.
/// </summary>
public sealed class GameData : IDisposable
{
    /// <summary>Excel tables that describe items, affixes and the stats stored in save files.</summary>
    public static readonly string[] ExcelTables =
    [
        "armor", "weapons", "misc", "itemstatcost", "itemtypes", "properties",
        "uniqueitems", "setitems", "sets", "runes", "gems",
        "magicprefix", "magicsuffix", "rareprefix", "raresuffix",
        "skills", "skilldesc",
        "levels", "monstats", "automagic", "charstats", "hireling", "inventory", "itemratio",
        "qualityitems", "cubemain", "lowqualityitems", "states",
    ];

    // Highest priority first. ProjectD2 ships its own patch_d2.mpq that shadows the
    // one in the game root, so both are listed and order decides the winner.
    private static readonly string[] ArchiveChain =
    [
        @"ProjectD2\pd2data.mpq",
        @"ProjectD2\pd2assets.mpq",
        @"ProjectD2\patch_d2.mpq",
        @"patch_d2.mpq",
        @"d2exp.mpq",
        @"d2data.mpq",
        // The characters' animations (DCC and COF), for the figure in its gear; last, as nothing else
        // lives there. Opening it reads two small tables, not its 266 MB.
        @"d2char.mpq",
        // The resolution mod PD2 ships (SlashDiablo's SGD2FreeRes): the editor's window frame is cut from
        // its panel pieces (Graphics/FrameArt). Its paths are its own (data\SGD2FreeResolution), so it
        // shadows nothing.
        @"ProjectD2\SGD2FreeRes.mpq",
    ];

    private readonly List<MpqArchive> _archives = [];
    private readonly string? _loose;

    public string GameRoot { get; }
    public IReadOnlyList<string> LoadedArchives => _archives.Select(a => a.Path).ToList();

    private GameData(string gameRoot, string? loose)
    {
        GameRoot = gameRoot;
        _loose = string.IsNullOrWhiteSpace(loose) ? null : loose;
    }

    /// <summary>
    /// Opens the archive chain, optionally behind a folder of loose files that wins over
    /// it.
    /// <para>
    /// The game does this itself: run with <c>-direct</c> it looks in a loose <c>data</c>
    /// tree before the archives. Here it is what lets a body of items be read with
    /// <b>its own season's tables</b> rather than the installed one's - which is not a
    /// nicety. Affix ids drift between seasons, so an older item measured against today's
    /// tables reports affixes it does not have: 4438 items of one pack came back 47% flagged
    /// and 575 of them as impossible, every one of them a real item the game had made.
    /// </para>
    /// </summary>
    /// <param name="looseFirst">
    /// A folder holding either a <c>data\global\excel</c> tree or a flat set of tables -
    /// which is what a table dump looks like, so both are tried.
    /// </param>
    public static GameData Open(string gameRoot, string? looseFirst = null)
    {
        var data = new GameData(gameRoot, looseFirst);
        try
        {
            foreach (var relative in ArchiveChain)
            {
                string full = Path.Combine(gameRoot, relative);
                if (File.Exists(full))
                    data._archives.Add(MpqArchive.Open(full));
            }
            if (data._archives.Count == 0)
                throw new FileNotFoundException($"No Diablo II archives found under '{gameRoot}'.");
            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    public static string ExcelPath(string table) => $@"data\global\excel\{table}.txt";

    /// <summary>
    /// An excel table's lines - decoded, carriage returns gone, split on newlines, the header first - or
    /// null where no archive has it. Read and decoded once per GameData and kept: some twenty loaders
    /// each read their table by hand, and three tables were read two or three times over.
    /// The array is shared; read it, never write to it.
    /// </summary>
    public string[]? Lines(string table)
    {
        lock (_lines)
        {
            if (_lines.TryGetValue(table, out var kept))
                return kept;

            string[]? lines = Read(ExcelPath(table)) is { } hit
                ? System.Text.Encoding.ASCII.GetString(hit.Data).Replace("\r", "").Split('\n')
                : null;
            _lines[table] = lines;
            return lines;
        }
    }

    /// <summary>The same table's lines split into cells on tabs, the header row first; empty where it is missing.</summary>
    public IReadOnlyList<string[]> Rows(string table)
    {
        lock (_rows)
        {
            if (_rows.TryGetValue(table, out var kept))
                return kept;

            IReadOnlyList<string[]> rows = Lines(table) is { } lines
                ? [.. lines.Select(line => line.Split('\t'))]
                : [];
            _rows[table] = rows;
            return rows;
        }
    }

    private readonly Dictionary<string, string[]?> _lines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string[]>> _rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An item's inventory graphic, named by the tables' "invfile" column.</summary>
    public static string ItemSpritePath(string invFile) => $@"data\global\items\{invFile}.dc6";

    /// <summary>
    /// The 256-colour palette inventory graphics are drawn with. Act 1's is what the
    /// inventory panel uses, and item colours do not vary by act.
    /// </summary>
    public const string PalettePath = @"data\global\palette\ACT1\pal.dat";

    /// <summary>
    /// Reads a file: from the loose folder where one was given and has it, otherwise from
    /// the first archive that does.
    /// </summary>
    public (byte[] Data, string Archive)? Read(string mpqPath)
    {
        if (_loose is not null)
        {
            // Either shape: the tree as an archive holds it, or the bare file name, since a
            // folder of tables dumped out of the archives is flat.
            foreach (string candidate in new[]
                     {
                         Path.Combine(_loose, mpqPath),
                         Path.Combine(_loose, Path.GetFileName(mpqPath)),
                     })
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        Served(mpqPath);
                        return (File.ReadAllBytes(candidate), Path.GetFileName(_loose));
                    }
                }
                catch (Exception)
                {
                    // A loose file that cannot be read is one the archives still can.
                }
            }
        }

        foreach (var archive in _archives)
        {
            var data = archive.ReadFile(mpqPath);
            if (data is not null)
            {
                Served(mpqPath);
                return (data, Path.GetFileName(archive.Path));
            }
        }
        return null;
    }

    private readonly HashSet<string> _served = new(StringComparer.OrdinalIgnoreCase);

    private void Served(string mpqPath)
    {
        lock (_served)
            _served.Add(mpqPath);
    }

    /// <summary>
    /// Every Excel table the archives say they hold, from each one's own <c>(listfile)</c>: PD2's
    /// archive lists its whole table set (93 in S13), the base game's theirs. Not read through
    /// <see cref="Read"/>, so asking does not count as serving anything.
    /// </summary>
    public IReadOnlyList<string> ListedTables()
    {
        var tables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archive in _archives)
        {
            if (archive.ReadFile("(listfile)") is not { } list)
                continue;

            foreach (string line in System.Text.Encoding.ASCII.GetString(list).Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith(@"data\global\excel\", StringComparison.OrdinalIgnoreCase)
                    && line.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(line.Trim());
                }
            }
        }

        return [.. tables];
    }

    /// <summary>
    /// Every path served so far, from a loose folder or an archive. Taken just after the tables are
    /// installed, it is exactly what a season is read from - what <see cref="SeasonSnapshot"/> keeps,
    /// so a season opened again from its snapshot never falls back to a newer archive's file.
    /// </summary>
    public IReadOnlyList<string> ServedPaths
    {
        get
        {
            lock (_served)
                return [.. _served];
        }
    }

    /// <summary>
    /// An excel table parsed once per GameData and kept, for readers that only read it - a character's
    /// skill tree asked three tables on every load, some 30 ms (measured). Shared: never
    /// write to its rows; the testbed overrides that edit a table take their own from ReadExcel.
    /// </summary>
    public TxtTable Excel(string table) => KeptExcel(table).Table;

    /// <summary>
    /// <see cref="Excel"/> with the archive it was read from - for a reader that says where its numbers came from.
    /// The stash's grid and a class's starting numbers were read and parsed from the archive anew on every rebuild of
    /// the shared stash and every Advanced Stats refresh.
    /// </summary>
    public (TxtTable Table, string Archive) KeptExcel(string table)
    {
        lock (_tables)
        {
            if (!_tables.TryGetValue(table, out var kept))
                _tables[table] = kept = ReadExcel(table);
            return kept;
        }
    }

    private readonly Dictionary<string, (TxtTable Table, string Archive)> _tables = new(StringComparer.OrdinalIgnoreCase);

    public (TxtTable Table, string Archive) ReadExcel(string table)
    {
        var result = Read(ExcelPath(table))
            ?? throw new FileNotFoundException($"'{table}.txt' is not present in any loaded archive.");
        return (TxtTable.Parse(table, result.Data), result.Archive);
    }

    public void Dispose()
    {
        foreach (var archive in _archives)
            archive.Dispose();
        _archives.Clear();
    }
}
