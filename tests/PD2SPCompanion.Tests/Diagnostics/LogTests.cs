using PD2SPCompanion.Core.Diagnostics;

namespace PD2SPCompanion.Tests.Diagnostics;

/// <summary>
/// The editor's log: entries say their level and area, a caught exception keeps
/// its stack under the class and member that caught it, and a full file is set aside. The log is
/// one for the process, so the asserts allow lines other tests write while it is open.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class LogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-log-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "editor.log");

    public void Dispose()
    {
        Log.Close();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [GameFact]
    public void Entries_carry_level_area_and_a_caught_stack()
    {
        Log.Open(File, "test header");
        Log.Info("strip", "Saved.");
        Log.Warning("strip", "No free space for it.");
        try
        {
            throw new InvalidOperationException("broke");
        }
        catch (InvalidOperationException problem)
        {
            Log.Caught(problem);
        }
        Log.Close();
        Log.Info("strip", "said after Close");

        string text = System.IO.File.ReadAllText(File);
        Assert.Contains("test header", text);
        Assert.Contains("INFO  [strip] Saved.", text);
        Assert.Contains("WARN  [strip] No free space for it.", text);
        Assert.Contains($"WARN  [{nameof(LogTests)}.{nameof(Entries_carry_level_area_and_a_caught_stack)}] broke", text);
        Assert.Contains("System.InvalidOperationException: broke", text);
        Assert.DoesNotContain("said after Close", text);
    }

    [GameFact]
    public void A_full_file_is_set_aside_and_only_the_kept_ones_stay()
    {
        Log.Open(File, "roll");
        string line = new('x', 10_000);
        for (int i = 0; i < 3 * Log.Cap / line.Length + 10; i++)
            Log.Info("fill", line);
        Log.Close();

        Assert.True(System.IO.File.Exists(Path.Combine(_folder, "editor.1.log")));
        Assert.True(System.IO.File.Exists(Path.Combine(_folder, $"editor.{Log.Kept}.log")));
        Assert.False(System.IO.File.Exists(Path.Combine(_folder, $"editor.{Log.Kept + 1}.log")));
        Assert.True(new FileInfo(File).Length <= Log.Cap + line.Length + 200);
    }
}
