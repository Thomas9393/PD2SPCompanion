using System.IO.Compression;

using PD2SPCompanion.Core.Storage;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// Settings' Report a problem: one zip in the data folder's
/// Reports - a page of what is running, the settings and the logs - and the open character's files and a copy of
/// the vault only where its tick is set, which is off as the program starts. Nothing is sent anywhere.
/// <para>
/// What the page makes, read back out of the zip. That the button shows the file in its folder is Explorer's, and
/// not asked here; what a report is made of in general is Core's (ProblemReportTests).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ProblemReportTests(Program program)
{
    private VP_ViewModel_Settings_L Settings => program.Get<VP_ViewModel_Settings_L>();

    private static List<string> Names(string zip)
    {
        using var read = ZipFile.OpenRead(zip);
        return [.. read.Entries.Select(entry => entry.FullName)];
    }

    private static string About(string zip)
    {
        using var read = ZipFile.OpenRead(zip);
        using var page = new StreamReader(read.GetEntry("about.txt")!.Open());
        return page.ReadToEnd();
    }

    [CorpusFact]
    public void A_report_is_the_about_page_and_the_settings_and_the_saves_go_in_only_where_asked()
    {
        if (!program.Ready())
            return;

        var made = new List<string>();
        try
        {
            program.On(() =>
            {
                Assert.False(Settings.ReportWithSaves, "the tick is off as the program starts");
                Assert.Equal("Logs and settings, in one zip", Settings.ReportText);

                string plain = Settings.MakeReport();
                made.Add(plain);
                Assert.Equal(EditorData.Reports, Path.GetDirectoryName(plain));
                Assert.Contains("about.txt", Names(plain));
                Assert.DoesNotContain(Names(plain), name => name.StartsWith("Saves/", StringComparison.Ordinal) || name.StartsWith("Vault/", StringComparison.Ordinal));

                string about = About(plain);
                Assert.StartsWith($"{Settings.AppName} {Settings.VersionLabel}", about);
                Assert.Contains("Play mode: ", about);
                Assert.Contains("Character open: yes", about);
                Assert.Contains($"Data folder: {Settings.DataFolder}", about);

                // Ticked: the open character's save and a copy of the vault with it.
                Settings.ReportWithSaves = true;
                Assert.Equal("Logs, settings, open character and vault, in one zip", Settings.ReportText);

                string whole = Settings.MakeReport();
                made.Add(whole);
                Assert.Contains("Saves/" + Path.GetFileName(program.Main.Session.SavePath), Names(whole));
                Assert.Contains(Names(whole), name => name.StartsWith("Vault/", StringComparison.Ordinal) && name.EndsWith(".pd2lib", StringComparison.Ordinal));
                Assert.DoesNotContain(Names(whole), name => name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));

                // The data folder's row says where it is - and how much it holds once that is counted.
                Assert.StartsWith(Settings.DataFolder, Settings.DataFolderText);
            });
        }
        finally
        {
            program.On(() => Settings.ReportWithSaves = false);
            foreach (string report in made)
            {
                try
                {
                    File.Delete(report);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
