using System.Text;

namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// A Diablo II excel table: tab-delimited, one header row, everything else a row
/// whose position matters — the game refers to many rows by index, not by name.
/// </summary>
public sealed class TxtTable
{
    private readonly Dictionary<string, int> _columnIndex;
    private readonly List<string[]> _rows;

    public string Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<string[]> Rows { get; }

    private TxtTable(string name, string[] columns, List<string[]> rows)
    {
        Name = name;
        Columns = columns;
        _rows = rows;
        Rows = rows;
        _columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
            _columnIndex.TryAdd(columns[i], i);
    }

    public static TxtTable Parse(string name, byte[] data)
    {
        // The tables are ASCII with CRLF line endings; a trailing blank line is normal.
        var lines = Encoding.ASCII.GetString(data).Split('\n');
        if (lines.Length == 0)
            throw new InvalidDataException($"'{name}.txt' is empty.");

        var columns = lines[0].TrimEnd('\r').Split('\t');
        var rows = new List<string[]>(lines.Length);
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0 && i == lines.Length - 1)
                continue;
            rows.Add(line.Split('\t'));
        }
        return new TxtTable(name, columns, rows);
    }

    /// <summary>
    /// Adds a row at the end, which is the only place a row can safely be added: the game
    /// refers to many rows by index, so anything inserted above would renumber the rest.
    /// </summary>
    public void Append(string[] cells) => _rows.Add(cells);

    public int IndexOfColumn(string column) => _columnIndex.TryGetValue(column, out int i) ? i : -1;

    public string Value(int row, string column)
    {
        int col = IndexOfColumn(column);
        if (col < 0)
            return string.Empty;
        var cells = Rows[row];
        return col < cells.Length ? cells[col] : string.Empty;
    }
}
