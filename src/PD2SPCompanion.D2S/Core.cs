using D2SLib.Model.Save;
using System;
using System.IO;

namespace D2SLib
{
    public class Core
    {
        /// <summary>
        /// The excel tables the parser reads bit widths and item types from.
        /// Set once at startup by PD2SPCompanion.Core.SaveTables from the installed
        /// game's MPQ chain. Upstream fell back to tables embedded in the assembly;
        /// that fallback is deliberately gone, because a bundled snapshot goes stale
        /// the day PD2 patches and mis-parses items silently.
        /// </summary>
        public static TXT TXT { get; set; }

        public static D2S ReadD2S(string path)
        {
            return D2S.Read(File.ReadAllBytes(path));
        }

        public static D2S ReadD2S(byte[] bytes)
        {
            return D2S.Read(bytes);
        }

        public static Item ReadItem(string path, UInt32 version)
        {
            return ReadItem(File.ReadAllBytes(path), version);
        }

        public static Item ReadItem(byte[] bytes, UInt32 version)
        {
            return Item.Read(bytes, version);
        }
        public static D2I ReadD2I(string path, UInt32 version)  // old version
        {
            return ReadD2I(File.ReadAllBytes(path), version, ".sss");
        }

        public static D2I ReadD2I(byte[] bytes, UInt32 version)  // old version
        {
            return D2I.Read(bytes, version, ".sss");
        }

        public static D2I ReadD2I(string path, UInt32 version, string type)
        {
            return ReadD2I(File.ReadAllBytes(path), version, type);
        }

        public static D2I ReadD2I(byte[] bytes, UInt32 version, string type)
        {
            return D2I.Read(bytes, version, type);
        }

        public static byte[] WriteD2S(D2S d2s)
        {
            return D2S.Write(d2s);
        }

        public static byte[] WriteItem(Item item, UInt32 version)
        {
            return Item.Write(item, version);
        }

        public static byte[] WriteD2I(D2I d2i, UInt32 version)  // old version
        {
            return D2I.Write(d2i, version, ".sss");
        }

        public static byte[] WriteD2I(D2I d2i, UInt32 version, string type)
        {
            return D2I.Write(d2i, version, type);
        }

    }
}
