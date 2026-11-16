using D2SLib;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Which .d2s layout a file turned out to use. Nothing in the file says which; the
/// format is identified by trying each and seeing which one round-trips.
/// </summary>
public enum SaveFormat
{
    Unknown,
    /// <summary>Vanilla: no reserved skill bytes, 9-bit stat ids.</summary>
    Vanilla,
    /// <summary>PD2 up to S8: 3 reserved skill bytes, 9-bit stat ids.</summary>
    Pd2Legacy,
    /// <summary>Current PD2: 3 reserved skill bytes, 10-bit stat ids.</summary>
    Pd2,

    /// <summary>
    /// Last season's PD2: the same layout, but item properties encoded with the widths and
    /// offsets the current tables keep in their <c>S&lt;n&gt;</c> columns.
    /// <para>
    /// This is what a character looks like the first time it is opened after a new season
    /// lands, and the difference is not small - every attribute moved from
    /// <c>Save Add 32</c> to <c>100</c> between S12 and S13, so one read under the wrong
    /// encoding is 68 points short on all four without a word.
    /// </para>
    /// </summary>
    Pd2LastSeason,
}

public sealed record VerifyResult(
    string Path,
    SaveFormat Format,
    bool Parsed,
    bool RoundTripped,
    int OriginalLength,
    int WrittenLength,
    int FirstDifference,
    int ItemCount,
    string? Error)
{
    public string Summary => !Parsed ? "PARSE FAIL"
        : RoundTripped ? "ok"
        : "ROUND-TRIP FAIL";
}

/// <summary>
/// Reads a save, writes it straight back out, and compares the bytes.
/// <para>
/// A byte-identical result proves the reader and writer are inverses of each other.
/// It does <b>not</b> prove the values were interpreted correctly: a reader using the
/// wrong bit widths paired with a writer using the same wrong widths reproduces the
/// input exactly. Correct interpretation is a separate question, checked against what
/// the game displays.
/// </para>
/// </summary>
public static class SaveVerifier
{
    /// <summary>When set, the re-serialized bytes of each successful parse are written here (debug).</summary>
    public static string? DumpPath { get; set; }

    // Current PD2 first: it is what this tool targets. The others are tried so an old
    // character can at least be identified and refused with a useful message.
    private static readonly SaveFormat[] Candidates =
        [SaveFormat.Pd2, SaveFormat.Pd2LastSeason, SaveFormat.Pd2Legacy, SaveFormat.Vanilla];

    /// <summary>
    /// Parses a save with whichever layout round-trips, and hands back both the parsed
    /// character and that verdict. Anything that fails to round-trip is refused: the
    /// tool will not show, let alone edit, a file it cannot reproduce. So is a file that
    /// only round-trips as <see cref="SaveFormat.Vanilla"/> or <see cref="SaveFormat.Pd2Legacy"/>:
    /// it reads back under today's tables, which are not its own, so the values are the
    /// misreading this class's summary warns of. <see cref="Verify"/> and <see cref="Explain"/>
    /// still name those layouts.
    /// </summary>
    public static (D2S Save, VerifyResult Result) Load(string path)
    {
        var loaded = LoadAnyLayout(path);
        if (loaded.Result.Format is not (SaveFormat.Vanilla or SaveFormat.Pd2Legacy))
            return loaded;

        Install(SaveFormat.Pd2);
        throw NotReadable(path);
    }

    /// <summary>
    /// <see cref="Load"/> without its refusal of the old layouts: for what reads an old season's file under that
    /// season's own tables, which are then the right ones - the command line's <c>legit</c> and <c>pastvault</c>,
    /// over the item packs. Nothing that shows a character for editing or writes one.
    /// </summary>
    public static (D2S Save, VerifyResult Result) LoadAnyLayout(string path)
    {
        var original = File.ReadAllBytes(path);

        // A character created but never played is written as the fixed header and nothing
        // else: no quests, waypoints, attributes or items. It is a perfectly valid file, so
        // saying it was written by another season would be wrong as well as unhelpful.
        if (original.Length <= SaveSummary.HeaderOnlyLength)
        {
            throw new InvalidDataException(
                L($"'{Path.GetFileName(path)}' never played. Require one game session with it."));
        }

        if (Pick(path, original) is { } picked)
        {
            Install(picked.Format);
            return (D2SLib.Core.ReadD2S(original), picked);
        }
        throw NotReadable(path);
    }

    // Saying what is wrong is half of it; the other half is what to do. A save from an
    // older season is not unreadable, it is being read with the wrong tables - the stat
    // widths moved. Pointed at that season's own tables, 114 of the item pack's saves
    // that this refused went on to round-trip byte-identical. Not always an older season, though: PhysSpear, saved by
    // Season 14 itself with three unidentified uniques in it, was a layout the editor did not read yet -
    // and the message said "likely an older PD2 season".
    private static InvalidDataException NotReadable(string path) =>
        new(L($"'{Path.GetFileName(path)}' not readable: an older PD2 season's file, or something this version cannot read yet."));

    /// <summary>
    /// Reads a PlugY stash - <c>.sss</c>, <c>.d2x</c> - under the stat-id width that
    /// reproduces the file exactly, and refuses one that neither width reproduces.
    /// <para>
    /// The width is not written down anywhere in the file. Vanilla and PD2 up to S8 use nine
    /// bits with 511 as the terminator; current PD2 uses ten, because its ItemStatCost fills
    /// the nine-bit space. Reading with the wrong one does not fail loudly - it walks the
    /// wrong number of bits and yields a different item - so the only honest test is whether
    /// writing it back gives the bytes we started with, which is the same rule
    /// <see cref="Load"/> applies to a character.
    /// </para>
    /// </summary>
    public static (D2I Stash, int StatIdBits) LoadStash(string path)
    {
        var original = File.ReadAllBytes(path);
        string extension = Path.GetExtension(path);

        // The parser's switches put back as they were once this is done (Globals.Use): reading or
        // writing this file never changes how the next file elsewhere is read.
        using var keep = Globals.Use(Globals.Layout);

        foreach (int bits in (int[])[9, 10])
        {
            try
            {
                Globals.ResetForFile(pd2Character: false, statIdBits: bits);
                var stash = D2SLib.Core.ReadD2I(original, StashVersion, extension);

                Globals.ResetForFile(pd2Character: false, statIdBits: bits);
                if (D2SLib.Core.WriteD2I(stash, StashVersion, extension).AsSpan()
                        .SequenceEqual(original))
                {
                    // Left installed, so the caller's own reads and writes use it.
                    Globals.ResetForFile(pd2Character: false, statIdBits: bits);
                    return (D2SLib.Core.ReadD2I(original, StashVersion, extension), bits);
                }
            }
            catch
            {
                // Tried the other width rather than reported: a stash that neither reads is
                // what the message below is for.
            }
        }

        throw new InvalidDataException(
            L($"'{Path.GetFileName(path)}' not readable: likely another PD2 season."));
    }

    /// <summary>The item version a PlugY stash is read at; stashes carry no header version.</summary>
    private const uint StashVersion = 0x60;

    /// <summary>Every attempt made for a file, in order — for diagnosing a failure.</summary>
    public static IReadOnlyList<VerifyResult> Explain(string path)
    {
        // The parser's switches put back as they were once this is done (Globals.Use): reading or
        // writing this file never changes how the next file elsewhere is read.
        using var keep = Globals.Use(Globals.Layout);
        var original = File.ReadAllBytes(path);
        return Candidates.Select(format => Attempt(path, original, format)).ToList();
    }

    public static VerifyResult Verify(string path)
    {
        // The parser's switches put back as they were once this is done (Globals.Use): reading or
        // writing this file never changes how the next file elsewhere is read.
        using var keep = Globals.Use(Globals.Layout);
        var original = File.ReadAllBytes(path);
        VerifyResult? firstParsed = null;

        try
        {
            if (Pick(path, original) is { } picked)
                return picked;
        }
        catch (InvalidDataException ambiguous)
        {
            // Reads both ways and its evidence does not say which: reported, not chosen.
            return Attempt(path, original, SaveFormat.Pd2) with { Format = SaveFormat.Unknown, RoundTripped = false, Error = ambiguous.Message };
        }

        foreach (var format in Candidates)
        {
            var result = Attempt(path, original, format);
            firstParsed ??= result.Parsed ? result : null;
        }

        // Nothing round-tripped. If some layout at least parsed, report that one;
        // otherwise the file is not readable with the installed season's tables, and
        // claiming a format would be a guess.
        if (firstParsed is not null)
            return firstParsed;
        var pd2 = Attempt(path, original, SaveFormat.Pd2);
        return pd2 with { Format = SaveFormat.Unknown };
    }

    /// <summary>
    /// The first layout the file round-trips under - except that a file reading cleanly as this
    /// season's <b>and</b> as last season's, with different values, is settled by its own evidence
    /// (<see cref="SeasonReading.IsLastSeason"/>), never by which was tried first. Null where none
    /// round-trips; refused where the evidence does not decide.
    /// </summary>
    private static VerifyResult? Pick(string path, byte[] original)
    {
        foreach (var format in Candidates)
        {
            var result = Attempt(path, original, format);
            if (!result.RoundTripped)
                continue;

            if (format == SaveFormat.Pd2 && SeasonReading.HasPreviousSeason)
            {
                var last = Attempt(path, original, SaveFormat.Pd2LastSeason);
                if (last.RoundTripped
                    && SeasonReading.IsLastSeason(Path.GetFileName(path), ItemsAs(original, format), ItemsAs(original, SaveFormat.Pd2LastSeason)))
                {
                    return last;
                }
            }

            return result;
        }

        return null;
    }

    /// <summary>A character's items - its own and its mercenary's - read under one layout.</summary>
    private static IReadOnlyList<Item> ItemsAs(byte[] original, SaveFormat format)
    {
        Install(format);
        var save = D2SLib.Core.ReadD2S(original);
        return [.. SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary)];
    }

    private static VerifyResult Attempt(string path, byte[] original, SaveFormat format)
    {
        try
        {
            Install(format);
            D2S save = D2SLib.Core.ReadD2S(original);
            int itemCount = save.PlayerItemList?.Count ?? 0;

            byte[] written = D2S.Write(save);
            if (DumpPath is not null)
                File.WriteAllBytes(DumpPath, written);
            int difference = FirstDifference(original, written);

            return new VerifyResult(path, format, Parsed: true, RoundTripped: difference < 0,
                original.Length, written.Length, difference, itemCount, Error: null);
        }
        catch (Exception ex)
        {
            // The first frame inside the save model says which section ran off the rails,
            // which is the only useful thing about these failures.
            string where = ex.StackTrace?
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Contains("D2SLib.Model")) ?? "";
            return new VerifyResult(path, format, Parsed: false, RoundTripped: false,
                original.Length, 0, -1, 0, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}  {where}");
        }
    }

    /// <summary>
    /// Puts the parser into one layout. One place, because the three switches have to agree:
    /// a format that got its skill bytes from one arm and its stat width from another read
    /// nothing at all.
    /// <para>
    /// <b>This is global state, and anything else that reads a file changes it.</b> The
    /// shared stash reads at its own widths, so after it has been touched the parser is set
    /// for a stash and not for a character - which is why a caller about to read or write a
    /// character says so first rather than assuming.
    /// </para>
    /// </summary>
    public static void Install(SaveFormat format) =>
        Globals.ResetForFile(
            pd2Character: format is SaveFormat.Pd2 or SaveFormat.Pd2LastSeason
                                    or SaveFormat.Pd2Legacy,
            statIdBits: format is SaveFormat.Pd2 or SaveFormat.Pd2LastSeason ? 10 : 9,
            previousSeasonStats: format is SaveFormat.Pd2LastSeason);

    /// <summary>Offset of the first differing byte, or -1 when the two are identical.</summary>
    private static int FirstDifference(byte[] left, byte[] right)
    {
        int shared = Math.Min(left.Length, right.Length);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
                return i;
        }
        return left.Length == right.Length ? -1 : shared;
    }
}
