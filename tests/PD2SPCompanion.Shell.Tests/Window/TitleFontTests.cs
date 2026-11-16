using Avalonia;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// The titles' font: Cinzel as shipped, and Settings says so; a Fonts folder that is not there,
/// one with no font file in it and one whose file is no font all leave the titles in Cinzel.
/// <para>
/// A font that does read is taken for as long as the program runs, with no way back - so a font put in the
/// folder, taken at the next start and named in Settings, is the UI case's to show
/// (title-font.font-in-the-folder-is-taken-at-the-next-start).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TitleFontTests(Program program)
{
    private VP_ViewModel_Settings_L Page => program.Get<VP_ViewModel_Settings_L>();

    /// <summary>
    /// The folder's fonts looked for as a start looks for them, in a folder of the test's own holding these
    /// files (none: the folder is not made), and what the titles are in afterwards.
    /// </summary>
    private void StartedWith(bool folder, params (string Name, string Text)[] files)
    {
        string fonts = Path.Combine(program.Folder, "fonts-" + Guid.NewGuid().ToString("N"));
        string? was = Environment.GetEnvironmentVariable("PD2SPC_FONTS");
        try
        {
            if (folder)
                Directory.CreateDirectory(fonts);
            foreach (var (name, text) in files)
                File.WriteAllText(Path.Combine(fonts, name), text);

            Environment.SetEnvironmentVariable("PD2SPC_FONTS", fonts);

            // Only a Debug build is told its folder; any other reads the one beside the program.
            if (TitleFont.Folder != fonts)
                return;

            program.On(() =>
            {
                var face = Application.Current!.Resources[TitleFont.ResourceKey];

                TitleFont.Use(Application.Current!);

                Assert.Equal("Cinzel", TitleFont.Family);
                Assert.False(TitleFont.FromFolder);
                Assert.Same(face, Application.Current!.Resources[TitleFont.ResourceKey]);
                Assert.Equal("Cinzel", Page.TitleFontText);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PD2SPC_FONTS", was);
            if (Directory.Exists(fonts))
                Directory.Delete(fonts, recursive: true);
        }
    }

    [CorpusFact]
    public void As_shipped_the_titles_are_in_Cinzel_and_Settings_says_so_with_nothing_of_the_folder()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.Equal("Cinzel", TitleFont.Shipped);
            Assert.Equal("Cinzel", TitleFont.Family);
            Assert.False(TitleFont.FromFolder);
            Assert.Equal("Cinzel", Page.TitleFontText);
            Assert.DoesNotContain("from the Fonts folder", Page.TitleFontText);
        });
    }

    [CorpusFact]
    public void No_Fonts_folder_or_one_with_no_font_file_in_it_leaves_the_titles_in_Cinzel()
    {
        if (!program.Ready())
            return;

        StartedWith(folder: false);
        StartedWith(folder: true);
        StartedWith(folder: true, ("readme.txt", "Put a font here."));
    }

    [CorpusFact]
    public void A_file_that_is_no_font_is_passed_over_and_the_titles_stay_in_Cinzel()
    {
        if (!program.Ready())
            return;

        StartedWith(folder: true, ("notafont.ttf", "not a font"));
        StartedWith(folder: true, ("notafont.otf", "not a font"));
    }
}
