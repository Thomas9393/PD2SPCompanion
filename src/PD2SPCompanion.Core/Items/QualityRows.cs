namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Turning a unique or set item's <c>FileIndex</c> into a row of <c>uniqueitems.txt</c> or
/// <c>setitems.txt</c>, and back.
/// <para>
/// <b>The two are not the same number.</b> Each table carries one marker row whose
/// <c>index</c> is <c>Expansion</c>, and the game does not count it - so every item from
/// that row on answers to the row before its own. Every other blank-code row is counted:
/// <c>Armor</c>, <c>Rings</c>, <c>Elite Uniques</c>, the disabled uniques. Only
/// <c>Expansion</c> is dropped.
/// </para>
/// <para>
/// Measured, not assumed. Against BetweenWalls' item pack read with its own tables, the raw
/// row matched the item's own base code for <b>320 of 950</b> uniques and <b>148 of 298</b>
/// sets; skipping the marker matched <b>939</b> and <b>296</b>. The remainder are real items
/// rather than indexing errors - upgraded bases, a <c>FileIndex</c> of 4095 meaning no row,
/// and rings pointing at a disabled one. No other offset fitted anything: -1, 0, +1 and +2
/// were all tried.
/// </para>
/// <para>
/// It went unnoticed because every unique and set on the only live character sits below the
/// markers, which are at <c>uniqueitems</c> row 129 and <c>setitems</c> row 62 in both S9
/// and S13. Found by the PD2ItemMigration session and confirmed here independently.
/// </para>
/// <para>
/// <b>The marker is found by name, never by number.</b> A season that adds a unique above
/// it moves it.
/// </para>
/// </summary>
public static class QualityRows
{
    /// <summary>The one row the game does not count.</summary>
    private const string Marker = "Expansion";

    /// <summary>
    /// Where the marker sits in this table, or -1 for a table that has none - in which case
    /// every row answers to its own number and nothing here changes anything.
    /// </summary>
    public static int MarkerIn(TxtTable table)
    {
        for (int row = 0; row < table.Rows.Count; row++)
        {
            if (string.Equals(table.Value(row, "index").Trim(), Marker,
                              StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return -1;
    }

    /// <summary>
    /// The <c>FileIndex</c> a table row answers to, or -1 for the marker itself, which
    /// answers to nothing and belongs in no lookup.
    /// <para>
    /// This is the direction the loaders want: walk the table once, ask each row what an
    /// item would call it, and key everything by that. Then no lookup has to convert, and
    /// no lookup can be forgotten.
    /// </para>
    /// </summary>
    public static int IndexOf(int row, int marker) =>
        marker < 0 || row < marker ? row
        : row == marker ? -1
        : row - 1;

    /// <summary>
    /// The table row a <c>FileIndex</c> names. The other direction, for the places that
    /// read a row out of the table rather than a dictionary built from it.
    /// </summary>
    public static int RowOf(int fileIndex, int marker) =>
        marker < 0 || fileIndex < marker ? fileIndex : fileIndex + 1;
}
