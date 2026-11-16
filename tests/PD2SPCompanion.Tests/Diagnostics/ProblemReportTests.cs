using System.IO.Compression;

using PD2SPCompanion.Core.Diagnostics;

namespace PD2SPCompanion.Tests.Diagnostics;

/// <summary>
/// A report of a problem (ProblemReport): the files it is
/// handed and a page of what is running, in one zip; a file that is not there is left out; and the folder is held
/// to its newest three.
/// </summary>
public class ProblemReportTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 20, 15, 0);

    private static string Folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pd2spc-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void The_files_handed_over_and_the_about_page_are_one_zip_and_a_missing_file_is_left_out()
    {
        string folder = Folder();
        try
        {
            string log = Path.Combine(folder, "editor.log");
            File.WriteAllText(log, "a line of the log");

            // The log held open for writing, as the program holds it while a report is made.
            using var written = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.Read);

            string reports = Path.Combine(folder, "Reports");
            string path = ProblemReport.Write(reports, "What is running" + Environment.NewLine,
                [(log, "Logs/editor.log"), (Path.Combine(folder, "none.log"), "Logs/none.log")], Now);

            Assert.Equal(Path.Combine(reports, "pd2spc-report-20261012-201500.zip"), path);
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.Equal(["Logs/editor.log", "about.txt"], zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
                using var about = new StreamReader(zip.GetEntry(ProblemReport.AboutName)!.Open());
                Assert.StartsWith("What is running", about.ReadToEnd());
                using var kept = new StreamReader(zip.GetEntry("Logs/editor.log")!.Open());
                Assert.Equal("a line of the log", kept.ReadToEnd());
            }

            // A second in the same second: under a name of its own.
            string again = ProblemReport.Write(reports, "again", [], Now);
            Assert.Equal(Path.Combine(reports, "pd2spc-report-20261012-201500-2.zip"), again);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_folder_is_held_to_its_newest_three()
    {
        string folder = Folder();
        try
        {
            for (int n = 0; n < 5; n++)
            {
                string path = ProblemReport.Write(folder, $"report {n}", [], Now.AddMinutes(n));
                File.SetLastWriteTimeUtc(path, Now.AddMinutes(n).ToUniversalTime());
            }

            // Something else in the folder is not a report, and stays.
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
            ProblemReport.Prune(folder);

            Assert.Equal(
                ["notes.txt", "pd2spc-report-20261012-201700.zip", "pd2spc-report-20261012-201800.zip", "pd2spc-report-20261012-201900.zip"],
                Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
