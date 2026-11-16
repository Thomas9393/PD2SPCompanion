using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace D2SLib.Model.TXT
{
    public class ItemStatCostTXT : TXTFile
    {
        // By id through an array: the lookup by the ID column's text made a string of every id asked, twice for
        // every property of every item written or read. The same row the
        // text lookup gives - the first carrying that id - and rebuilt when the rows change, as that index is.
        public TXTRow this[int i]
        {
            get
            {
                if (_byIdOver != Rows.Count)
                {
                    var byId = new List<TXTRow>();
                    foreach (var row in Rows)
                    {
                        string key;
                        try { key = row["ID"].Value?.Trim(); }
                        catch (IndexOutOfRangeException) { continue; }

                        if (!int.TryParse(key, out int id) || id < 0 || id.ToString() != key)
                            continue;
                        while (byId.Count <= id)
                            byId.Add(null);
                        if (byId[id] == null)
                            byId[id] = row;
                    }

                    _byId = byId.ToArray();
                    _byIdOver = Rows.Count;
                }

                return i >= 0 && i < _byId.Length ? _byId[i] : null;
            }
        }

        private TXTRow[] _byId = Array.Empty<TXTRow>();
        private int _byIdOver = -1;

        public TXTRow this[string i] => this.GetByColumnAndValue("Stat", i);

        public static ItemStatCostTXT Read(Stream data)
        {
            ItemStatCostTXT itemStatCostTXT = new ItemStatCostTXT();
            itemStatCostTXT.ReadTXTData(data);
            return itemStatCostTXT;
        }
        public static ItemStatCostTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file))
            {
                return Read(stream);
            }
        }
    
    }
    /// <summary>
    /// Where a property's three encoding numbers are read from - its width, its offset and
    /// the width of its parameter.
    /// <para>
    /// The current table carries the <b>previous</b> season's as well, in columns suffixed
    /// with that season: <c>Save Bits S12</c> beside <c>Save Bits</c>. PD2 ships them so the
    /// game can read last season's characters at rollover. The suffix is found by pattern
    /// and never hardcoded, because next season it will say S13.
    /// </para>
    /// </summary>
    public static class StatEncoding
    {
        private static string _previousSeason = "";

        /// <summary>The season whose columns this table carries, or "" for none.</summary>
        public static string PreviousSeason => _previousSeason;

        /// <summary>
        /// Looks for the columns named "Save Bits S&lt;number&gt;" and remembers the newest suffix.
        /// Called when the tables are installed. The newest, not the first: S14's table carries
        /// S12's columns still and S13's after them (the open beta), and the first
        /// read every S13 item with S12's widths - and named the installed season S13.
        /// </summary>
        public static void Discover(TXTFile table)
        {
            _previousSeason = "";
            if (table?.Columns == null) return;

            int newest = -1;

            foreach (var column in table.Columns.Keys)
            {
                if (!column.StartsWith("Save Bits S", StringComparison.OrdinalIgnoreCase))
                    continue;

                string suffix = column.Substring("Save Bits ".Length).Trim();

                // Only a season marker - S12, S13 - not some other column that happens to
                // begin the same way.
                if (suffix.Length < 2 || char.ToUpperInvariant(suffix[0]) != 'S') continue;
                bool digits = true;
                for (int i = 1; i < suffix.Length; i++)
                    if (!char.IsDigit(suffix[i])) { digits = false; break; }

                if (!digits || !int.TryParse(suffix.Substring(1), out int number) || number <= newest) continue;

                newest = number;
                _previousSeason = suffix;
            }
        }

        public static Int32 SaveBits(TXTRow row) => Number(row, "Save Bits");

        public static Int32 SaveAdd(TXTRow row) => Number(row, "Save Add");

        public static Int32 SaveParamBits(TXTRow row) => Number(row, "Save Param Bits");

        /// <summary>
        /// The previous season's value where one is asked for and the row carries it -
        /// ninety of the rows do not, and those answer with the live column either way.
        /// </summary>
        private static Int32 Number(TXTRow row, string column)
        {
            if (Globals.previous_season_stats && _previousSeason.Length > 0 && row.Columns != null)
            {
                // "Save Add S12", but "Save Adds S13" - PD2's own spelling of the S14 table.
                foreach (string older in new[] { column + " " + _previousSeason, column + "s " + _previousSeason })
                {
                    if (row.Columns.ContainsKey(older)
                        && row[older].Value != null && row[older].Value.Trim().Length > 0)
                    {
                        return row[older].ToInt32();
                    }
                }
            }

            return row[column].ToInt32();
        }
    }
}
