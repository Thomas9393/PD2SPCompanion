using System.Security.Cryptography;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Whether the installed PD2 is the build this editor last checked the saves against, and the check
/// itself, of the utmost importance.
/// <para>
/// The editor reads PD2's tables live, so a patch or a new season changes how every save is read and
/// written, and a wrong bit width writes a save the game cannot load. A build is the files that decide
/// that - the tables' archive and the mod's code - and a new one is checked before anything is
/// written: every character and shared stash in the save folder must read back byte for byte.
/// </para>
/// <para>
/// Round-tripping proves the reader and writer agree, not that the values are read as the game reads
/// them (see <see cref="SaveVerifier"/>); the new-season checklist covers the rest.
/// </para>
/// </summary>
public static class PatchGuard
{
    /// <summary>
    /// The files a build is: the tables' archive, the mod's code, its patch archive. Not the loose
    /// <c>ProjectD2\data</c> tree, where the Experimental page installs its own files.
    /// </summary>
    public static readonly string[] Parts =
        [@"ProjectD2\pd2data.mpq", @"ProjectD2\ProjectDiablo.dll", @"ProjectD2\patch_d2.mpq"];

    /// <summary>
    /// For tests: a build to report instead of the installed one - or, starting with <c>@</c>, a file
    /// whose text is the build, so a test can change it under a running editor.
    /// </summary>
    public const string FakeBuild = "PD2SPC_FAKE_BUILD";

    /// <summary>For tests: the file name of a save the check is to call unreadable.</summary>
    public const string FakeUnreadable = "PD2SPC_FAKE_UNREADABLE";

    /// <summary>
    /// The installed build's fingerprint: a digest over each part's own digest, in order. Empty where
    /// none of the parts could be read - an unknown build, which is never called a match.
    /// </summary>
    public static string Fingerprint(string gameRoot)
    {
        if (Environment.GetEnvironmentVariable(FakeBuild) is { Length: > 0 } fake)
        {
            if (!fake.StartsWith('@'))
                return fake;
            try { return File.ReadAllText(fake[1..]).Trim(); }
            catch (Exception) { return ""; }
        }

        // The parts as they stand - length and last write - and the digest is worked out again only when one has moved:
        // it is some 8 ms of reading 8.6 MB, and it was asked after every autosave (the save folder's watcher hears the
        // editor's own write). A patch writes its files anew.
        string stamp = gameRoot + "|" + StampOf(gameRoot);
        lock (KeptGate)
        {
            if (_keptStamp == stamp)
                return _keptPrint;
        }

        string print = Digest(gameRoot);
        lock (KeptGate)
            (_keptStamp, _keptPrint) = (stamp, print);
        return print;
    }

    private static readonly object KeptGate = new();

    /// <summary>The last fingerprint worked out, and the install and parts' stamp it was worked out for.</summary>
    private static string _keptStamp = "", _keptPrint = "";

    /// <summary>Each part's length and last write time; empty for a part that is not there.</summary>
    private static string StampOf(string gameRoot)
    {
        var stamp = new System.Text.StringBuilder();
        foreach (string part in Parts)
        {
            try
            {
                var file = new FileInfo(Path.Combine(gameRoot, part));
                stamp.Append(file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks};" : ";");
            }
            catch (Exception)
            {
                stamp.Append("?;");
            }
        }

        return stamp.ToString();
    }

    private static string Digest(string gameRoot)
    {
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int read = 0;

        foreach (string part in Parts)
        {
            try
            {
                string path = Path.Combine(gameRoot, part);
                if (!File.Exists(path))
                    continue;

                using var file = File.OpenRead(path);
                whole.AppendData(System.Text.Encoding.ASCII.GetBytes(part));
                whole.AppendData(SHA256.HashData(file));
                read++;
            }
            catch (Exception)
            {
                // A part that cannot be read is left out; none read leaves the build unknown.
            }
        }

        return read == 0 ? "" : Convert.ToHexString(whole.GetHashAndReset())[..16].ToLowerInvariant();
    }

    /// <summary>One file checked, and what is wrong with it - null when it reads back exactly.</summary>
    public sealed record Checked(string File, string? Problem)
    {
        public bool Good => Problem is null;
    }

    /// <summary>
    /// Every character and shared stash in <paramref name="saveFolder"/>, read and written back in
    /// memory with the tables now installed. Nothing is written to disk. A character created and never
    /// played is only a header and is passed over, as the editor passes over it everywhere.
    /// <para>
    /// Each read runs in its own parser layout scope (D2SLib.Globals.Use) and puts the switches back.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Checked> Verify(string saveFolder)
    {
        var results = new List<Checked>();
        if (!Directory.Exists(saveFolder))
            return results;

        string? fake = Environment.GetEnvironmentVariable(FakeUnreadable);

        foreach (string path in Directory.EnumerateFiles(saveFolder, "*.d2s").Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);
            string? problem;
            try
            {
                if (new FileInfo(path).Length <= SaveSummary.HeaderOnlyLength)
                    continue;

                var result = SaveVerifier.Verify(path);
                problem = result.RoundTripped ? null : result.Error ?? L("does not read back exactly");
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }

            if (string.Equals(fake, name, StringComparison.OrdinalIgnoreCase))
                problem = L("does not read back exactly (test)");

            results.Add(new Checked(name, problem));
        }

        foreach (string path in Directory.EnumerateFiles(saveFolder, "*.stash").Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);
            string? problem;
            try
            {
                SharedStash.Read(path);
                problem = null;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }

            if (string.Equals(fake, name, StringComparison.OrdinalIgnoreCase))
                problem = L("does not read back exactly (test)");

            results.Add(new Checked(name, problem));
        }

        return results;
    }
}
