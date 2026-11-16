namespace D2SLib
{
    /// <summary>
    /// Parser-wide switches carried over from PD2-Converter, trimmed to what the save
    /// model actually reads.
    /// <para>
    /// These are static, which is a hazard: <see cref="space"/> is filled during a read
    /// and consumed during the next write, so reading file A then file B before writing A
    /// writes B's bytes into A. Safe only when each file is read and written as one unit.
    /// Kept static for now so this vendored code stays diffable against upstream; see
    /// docs/TECHLOG.md.
    /// </para>
    /// </summary>
    public static class Globals
    {
        /// <summary>
        /// PD2 characters reserve 3 bytes ahead of the player item list for its extra
        /// skills. Vanilla saves do not. There is no marker in the file, so the format is
        /// determined by trying one and falling back to the other.
        /// </summary>
        public static bool pd2_char_formatting = false;

        /// <summary>Online PD2 .stash / .stash.hc files carry no "JM" item list header.</summary>
        public static bool pd2_stash_formatting = false;

        /// <summary>The 3 reserved PD2 bytes, preserved verbatim between read and write.</summary>
        public static byte[] space = new byte[0];

        /// <summary>
        /// Width of the stat id field in an item's property list. Vanilla and PD2 up to
        /// S8 use 9 bits, with 511 as the terminator. Current PD2 uses 10 bits: its
        /// ItemStatCost.txt fills all 511 rows of the 9-bit space, so adding any stat at
        /// all required widening the field. The terminator is always all-ones.
        /// </summary>
        public static int stat_id_bits = 9;

        public static ushort StatIdTerminator => (ushort)((1 << stat_id_bits) - 1);

        /// <summary>
        /// Read and write item properties with the <b>previous</b> season's encoding, which
        /// the current tables carry in their own columns - <c>Save Bits S12</c> and its two
        /// companions in the S13 tables.
        /// <para>
        /// PD2 ships those so the game can still read last season's characters at rollover,
        /// and they are not cosmetic: 117 of the 421 rows that carry them differ, and the
        /// differences are offsets rather than tweaks. Every attribute moved from
        /// <c>Save Add 32</c> to <c>100</c>, so a character read under the wrong one is 68
        /// points short on all four, quietly.
        /// </para>
        /// <para>
        /// There is no marker in a save saying which it is, so the format is settled the way
        /// every other one here is: try it, and keep whichever writes back byte-identical.
        /// </para>
        /// </summary>
        public static bool previous_season_stats = false;

        public static bool writeConsole_D2SRead = false;
        public static bool writeConsole_ItemsRead = false;
        public static bool writeConsole_ItemsReadComplete = false;
        public static bool writeConsole_Stash = false;

        /// <summary>The switches as they stand now, to be put back later or given to a save.</summary>
        public static ParserLayout Layout =>
            new(pd2_char_formatting, stat_id_bits, previous_season_stats, pd2_stash_formatting);

        /// <summary>
        /// Sets the switches for one read or write and puts back what was there when the scope ends:
        /// <c>using var _ = Globals.Use(layout);</c>. A read in one place then no longer changes how
        /// the next read elsewhere is parsed.
        /// </summary>
        public static LayoutScope Use(ParserLayout layout)
        {
            var was = Layout;
            Apply(layout);
            return new LayoutScope(was);
        }

        internal static void Apply(ParserLayout layout)
        {
            pd2_char_formatting = layout.Pd2Character;
            pd2_stash_formatting = layout.Pd2Stash;
            stat_id_bits = layout.StatIdBits;
            previous_season_stats = layout.PreviousSeasonStats;
        }

        /// <summary>
        /// The item version stamps the installed season and the one before write ("105", "103"), where known - set
        /// with the season's (<c>SeasonReading.SetStamps</c>). With both known an item's properties are read and
        /// written in the encoding of <b>its own</b> stamp (<see cref="ItemEra"/>), whatever the file's switch says.
        /// </summary>
        public static string? current_season_stamp, previous_season_stamp;

        /// <summary>
        /// The encoding for one item's properties, by its stamp, until the scope ends - as the game reads them: an S14 file
        /// holds S13 items beside its own and reads each with its season's columns (NewS: ten 105 items and a 103 Bonehew;
        /// ShockSacri: 89 at 103 and 6 at 105). A stamp that is neither season's leaves the file's switch.
        /// </summary>
        public static EraScope ItemEra(string? stamp)
        {
            bool was = previous_season_stats;
            if (stamp is { Length: > 0 } && current_season_stamp is { } current && previous_season_stamp is { } previous
                && current != previous)
            {
                if (stamp == previous)
                    previous_season_stats = true;
                else if (stamp == current)
                    previous_season_stats = false;
            }

            return new EraScope(was);
        }

        /// <summary>Puts the file's switch back after one item's properties (<see cref="ItemEra"/>).</summary>
        public readonly struct EraScope(bool was) : System.IDisposable
        {
            public void Dispose() => previous_season_stats = was;
        }

        /// <summary>Resets the per-file parser state. Call before reading each save.</summary>
        public static void ResetForFile(bool pd2Character, int statIdBits = 9,
                                        bool previousSeasonStats = false)
        {
            pd2_char_formatting = pd2Character;
            pd2_stash_formatting = false;
            stat_id_bits = statIdBits;
            previous_season_stats = previousSeasonStats;
            space = new byte[0];
        }
    }

    /// <summary>
    /// How a file's bytes are laid out, as the parser's switches say it: a PD2 character or not, the
    /// stat id width, last season's stat encoding, the online stash form. Kept on a save as it was read
    /// (<see cref="Model.Save.D2S.Layout"/>), so writing it needs nobody to remember to set it.
    /// </summary>
    public sealed record ParserLayout(bool Pd2Character, int StatIdBits, bool PreviousSeasonStats, bool Pd2Stash = false);

    /// <summary>Puts the switches back as they were when <see cref="Globals.Use"/> was called.</summary>
    public readonly struct LayoutScope : System.IDisposable
    {
        private readonly ParserLayout? _was;

        internal LayoutScope(ParserLayout was) => _was = was;

        public void Dispose()
        {
            if (_was is not null)
                Globals.Apply(_was);
        }
    }
}
