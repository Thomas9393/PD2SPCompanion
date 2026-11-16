using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// Settings' Sounds, in the Display section: off, a press is still a press but
/// makes no sound; on again, it does. The choice is kept in the settings file.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SoundsToggleTests(Program program)
{
    [CorpusFact]
    public void Sounds_off_silences_the_click_and_is_kept()
    {
        if (!program.Ready())
            return;

        var sound = (SFXManager)program.Get<ISFXManager>();
        var settings = program.Get<EditorSettings>();
        try
        {
            program.On(() =>
            {
                settings.Sounds = false;
                int pressed = sound.Presses, sounded = sound.Sounded;
                sound.Method_ButtonSFX_Press();
                Assert.Equal((pressed + 1, sounded), (sound.Presses, sound.Sounded));
                Assert.Matches(@"""Sounds""\s*:\s*false", File.ReadAllText(EditorSettings.FilePath));

                settings.Sounds = true;
                sound.Method_ButtonSFX_Press();
                Assert.Equal(sounded + 1, sound.Sounded);
            });
        }
        finally
        {
            program.On(() => settings.Sounds = true);
        }
    }
}
