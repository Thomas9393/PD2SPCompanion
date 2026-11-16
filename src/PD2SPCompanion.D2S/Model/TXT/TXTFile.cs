using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace D2SLib.Model.TXT
{
    public abstract class TXTFile
    {
        public Dictionary<string, int> Columns { get; set; }
        public List<TXTRow> Rows { get; set; }

        protected void ReadTXTData(Stream data)
        {
            
            Columns = new Dictionary<string, int>();
            Rows = new List<TXTRow>();
            using (StreamReader reader = new StreamReader(data))
            {
                //skip header
                var columns = reader.ReadLine().Split('\t');

                // By its place: a repeated header keeps its first, and the rest keep theirs, where counting only the
                // new ones shifted every column after it.
                for (int idx = 0; idx < columns.Length; idx++)
                    Columns.TryAdd(columns[idx], idx);
                while (reader.Peek() >= 0)
                {
                    Rows.Add(new TXTRow(Columns, reader.ReadLine().Split('\t')));
                }
            }
        }

        // A column that has been looked up once, as value -> first row carrying it.
        //
        // This was a scan of every row, trimming every cell, and it is on the path for
        // every item that is read and every time an item's size is asked for: one
        // GetByCode walks armor, weapons and misc in turn, about 1300 rows. Indexing the
        // column the first time it is asked about turns that into a lookup.
        //
        // Rows are read once from the tables and not added to, but the count is watched
        // anyway so an index can never outlive the rows it was built from.
        private readonly Dictionary<string, Dictionary<string, TXTRow>> _indexed =
            new Dictionary<string, Dictionary<string, TXTRow>>();

        private int _indexedOver = -1;

        public TXTRow GetByColumnAndValue(string name, string value)
        {
            // No value is not a lookup failure waiting to happen - it is simply no row. An
            // ear has no code, and asking about it used to throw from inside the trim.
            if (value is null)
                return null;

            if (_indexedOver != Rows.Count)
            {
                _indexed.Clear();
                _indexedOver = Rows.Count;
            }

            if (!_indexed.TryGetValue(name, out var index))
            {
                index = new Dictionary<string, TXTRow>(StringComparer.Ordinal);

                foreach (var row in Rows)
                {
                    string key;

                    try
                    {
                        key = row[name].Value.Trim();
                    }
                    catch (IndexOutOfRangeException)
                    {
                        // A short row, which the scan would have walked past.
                        continue;
                    }

                    // The scan answered with the first row that matched, and the tables do
                    // carry repeated values. First still wins.
                    if (!index.ContainsKey(key))
                        index.Add(key, row);
                }

                _indexed.Add(name, index);
            }

            return index.TryGetValue(value.Trim(), out var found) ? found : null;
        }
    }
    public class TXTRow
    {
        public Dictionary<string, int> Columns { get; set; }
        public TXTCell[] Data { get; set; }

        public TXTCell this[int i] => this.GetByIndex(i);
        public TXTCell this[string i] => this.GetByColumn(i);

        public TXTRow(Dictionary<string, int> columns, string[] data)
        {
            Columns = columns;
            Data = data.Select(e => new TXTCell(e)).ToArray(); 
        }

        public TXTCell GetByIndex(int idx)
        {
            return Data[idx];
        }

        public TXTCell GetByColumn(string col)
        {
            return GetByIndex(Columns[col]);
        }

        /// <summary>
        /// The cell under a column, or null where this table has no such column - misc.txt has no
        /// reqstr, armor.txt no belt for a weapon. The indexer throws there, and requirement checks
        /// caught it some five thousand times a minute (measured).
        /// </summary>
        public TXTCell Get(string col)
        {
            return Columns.TryGetValue(col, out int idx) && idx < Data.Length ? Data[idx] : null;
        }
    }

    public class TXTCell
    {
        public string Value { get; set; }

        public Int32 ToInt32()
        {
            Int32 ret = 0;
            Int32.TryParse(Value, out ret);
            return ret;
        }

        public UInt32 ToUInt32()
        {
            UInt32 ret = 0;
            UInt32.TryParse(Value, out ret);
            return ret;
        }

        public UInt16 ToUInt16()
        {
            UInt16 ret = 0;
            UInt16.TryParse(Value, out ret);
            return ret;
        }

        public Int16 ToInt16()
        {
            Int16 ret = 0;
            Int16.TryParse(Value, out ret);
            return ret;
        }

        public bool ToBool()
        {
            return ToInt32() == 1;
        }
        public TXTCell(string value)
        {
            Value = value;
        }
    }
}
