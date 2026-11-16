using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// A settings file that will not read: the program opens on the defaults, as it always did,
/// but the file is copied aside before anything can write over it, and the strip is told where. On the run's own settings
/// file, written back as the program's settings have it.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SettingsFileTests(Program program)
{
    /// <summary>The run's settings file and any copy of it put back when done.</summary>
    private void PutBack()
    {
        File.Delete(EditorSettings.FilePath + EditorSettings.UnreadableSuffix);
        program.On(() =>
        {
            var own = program.Get<EditorSettings>();
            own.Save();
            own.Apply();
        });
    }

    [CorpusFact]
    public void A_file_that_does_not_parse_is_copied_aside_and_said_once_before_anything_writes_over_it()
    {
        if (!program.Ready())
            return;

        const string kept = "{ \"GameFolder\": \"somewhere\", \"Language\": 12, broken";
        try
        {
            File.WriteAllText(EditorSettings.FilePath, kept);

            var settings = EditorSettings.Load();

            Assert.Equal(kept, File.ReadAllText(EditorSettings.FilePath + EditorSettings.UnreadableSuffix));
            Assert.Equal(EditorSettings.FilePath + EditorSettings.UnreadableSuffix, settings.TakeUnreadableCopy());
            Assert.Null(settings.TakeUnreadableCopy());

            // What a first change writes goes over the file, the copy staying.
            settings.Save();
            Assert.NotEqual(kept, File.ReadAllText(EditorSettings.FilePath));
            Assert.Equal(kept, File.ReadAllText(EditorSettings.FilePath + EditorSettings.UnreadableSuffix));
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void A_file_held_by_another_program_is_not_written_over_until_it_can_be_copied()
    {
        if (!program.Ready())
            return;

        const string kept = "{ \"LastRunVersion\": \"held\" }";
        try
        {
            File.WriteAllText(EditorSettings.FilePath, kept);

            EditorSettings settings;
            using (new FileStream(EditorSettings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                settings = EditorSettings.Load();
                settings.Save();
            }

            // Let go: a save that had got in meanwhile would have left the defaults here.
            Assert.Equal(kept, File.ReadAllText(EditorSettings.FilePath));
            Assert.False(File.Exists(EditorSettings.FilePath + EditorSettings.UnreadableSuffix));

            settings.Save();

            Assert.Equal(kept, File.ReadAllText(EditorSettings.FilePath + EditorSettings.UnreadableSuffix));
            Assert.NotEqual(kept, File.ReadAllText(EditorSettings.FilePath));
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void A_good_file_and_no_file_keep_no_copy()
    {
        if (!program.Ready())
            return;

        try
        {
            PutBack();
            Assert.Null(EditorSettings.Load().TakeUnreadableCopy());
            Assert.False(File.Exists(EditorSettings.FilePath + EditorSettings.UnreadableSuffix));

            File.Delete(EditorSettings.FilePath);
            Assert.Null(EditorSettings.Load().TakeUnreadableCopy());
        }
        finally
        {
            PutBack();
        }
    }
}
