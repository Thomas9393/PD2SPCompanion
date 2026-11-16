using D2SLib.Model.Huffman;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace D2SLib.Model.TXT
{
    //collections or ArmorTXT MiscTXT WeaponsTXT with helper methods
    public class ItemsTXT
    {
        public ArmorTXT ArmorTXT {get; set;}
        public WeaponsTXT WeaponsTXT { get; set; }
        public MiscTXT MiscTXT { get; set; }
        public UniqueTXT UniqueTXT { get; set; }
        public SetItemsTXT SetItemsTXT { get; set; }
        public RunewordTXT RunewordTXT { get; set; }

        private HuffmanTree _ItemCodeTree = null;
        public HuffmanTree ItemCodeTree
        {
            get
            {
                if(_ItemCodeTree == null)
                {
                    _ItemCodeTree = InitializeHuffmanTree();
                }
                return _ItemCodeTree;
            }
            set
            {
                _ItemCodeTree = value;
            }
        }

        public TXTRow this[string i] => this.GetByCode(i);

        public TXTRow GetByCode(string code)
        {
            return ArmorTXT[code] ??
                WeaponsTXT[code] ??
                MiscTXT[code];
        }

        public bool IsArmor(string code)
        {
            return ArmorTXT[code] != null;
        }

        public bool IsWeapon(string code)
        {
            return WeaponsTXT[code] != null;
        }

        public bool IsMisc(string code)
        {
            return MiscTXT[code] != null;
        }

        private HuffmanTree InitializeHuffmanTree()
        {
            /*
            List<string> items = new List<string>();
            foreach(TXTRow row in ArmorTXT.Rows)
            {
                items.Add(row["code"]);
            }
            foreach (TXTRow row in WeaponsTXT.Rows)
            {
                items.Add(row["code"]);
            }
            foreach (TXTRow row in MiscTXT.Rows)
            {
                items.Add(row["code"]);
            }
            */
            var itemCodeTree = new HuffmanTree();
            itemCodeTree.Build(new List<string>());
            return itemCodeTree;
        }

    }

    public class ArmorTXT : TXTFile
    {
        public TXTRow this[string i] => this.GetByColumnAndValue("code", i);

        public static ArmorTXT Read(Stream data)
        {
            ArmorTXT armorTXT = new ArmorTXT();
            armorTXT.ReadTXTData(data);
            return armorTXT;
        }
        public static ArmorTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file))
            {
                return Read(stream);
            }
        }
    }
    public class WeaponsTXT : TXTFile
    {
        public TXTRow this[string i] => this.GetByColumnAndValue("code", i);

        public static WeaponsTXT Read(Stream data)
        {
            WeaponsTXT weaponsTXT = new WeaponsTXT();
            weaponsTXT.ReadTXTData(data);
            return weaponsTXT;
        }
        public static WeaponsTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file))
            {
                return Read(stream);
            }
        }
    }
    public class MiscTXT : TXTFile
    {
        public TXTRow this[string i] => this.GetByColumnAndValue("code", i);

        public static MiscTXT Read(Stream data)
        {
            MiscTXT miscTXT = new MiscTXT();
            miscTXT.ReadTXTData(data);
            return miscTXT;
        }
        public static MiscTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file))
            {
                return Read(stream);
            }
        }
    }

    /// <summary>
    /// uniqueitems.txt. An item's FileIndex is a row number here, so rows are kept
    /// exactly as the file has them - including the "Expansion" separator and any blank
    /// row. Dropping those shifts every row after them and silently renames items.
    /// Whether the game counts them is verified per item by checking that the resolved
    /// row's "code" matches the item's own code.
    /// </summary>
    public class UniqueTXT : TXTFile
    {
        public TXTRow this[string i] => this.GetByColumnAndValue("code", i);

        public static UniqueTXT Read(Stream data)
        {
            UniqueTXT txt = new UniqueTXT();
            txt.ReadTXTData(data);
            return txt;
        }
        public static UniqueTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file)) return Read(stream);
        }
    }

    /// <summary>setitems.txt. Same row-index rule as <see cref="UniqueTXT"/>.</summary>
    public class SetItemsTXT : TXTFile
    {
        public TXTRow this[string i] => this.GetByColumnAndValue("index", i);

        public static SetItemsTXT Read(Stream data)
        {
            SetItemsTXT txt = new SetItemsTXT();
            txt.ReadTXTData(data);
            return txt;
        }
        public static SetItemsTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file)) return Read(stream);
        }
    }

    /// <summary>runes.txt - runeword definitions, matched by their socketed rune codes.</summary>
    public class RunewordTXT : TXTFile
    {
        public static RunewordTXT Read(Stream data)
        {
            RunewordTXT txt = new RunewordTXT();
            txt.ReadTXTData(data);
            return txt;
        }
        public static RunewordTXT Read(string file)
        {
            using (Stream stream = File.OpenRead(file)) return Read(stream);
        }
    }
}
