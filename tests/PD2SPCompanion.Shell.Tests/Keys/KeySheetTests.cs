using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Keys;

/// <summary>
/// The sheet of keys and gestures (KeySheetService): not shown as the program
/// opens, shown and put away again by what F1 asks, opened by Settings' About card and closed by its OK - and
/// naming the gestures that had no one place: the menu's keys (a right click's five chords, until they became
/// the menu's rows), Ctrl and Shift on a stepper, the drags.
/// <para>
/// What the one sheet holds and what opens and shuts it. That F1 reaches it from anywhere, that Esc closes it
/// before anything under it, and that it is drawn over the window with its rows, are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class KeySheetTests(Program program)
{
    private static KeySheetService Sheet => KeySheetService.Shared;

    /// <summary>What the sheet says a key does; null where it does not name the key under that heading.</summary>
    private static string? Does(string keys, string under) =>
        Sheet.Left.Concat(Sheet.Right).FirstOrDefault(group => group.Title == under)?.Keys.FirstOrDefault(key => key.Keys == keys)?.Does;

    [CorpusFact]
    public void It_is_not_shown_until_asked_for_and_its_key_shows_it_and_puts_it_away()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Assert.Same(Sheet, program.Main.Keys);
                Assert.False(Sheet.IsOpen);

                Sheet.Toggle();
                Assert.True(Sheet.IsOpen);

                Sheet.Toggle();
                Assert.False(Sheet.IsOpen);
            });
        }
        finally
        {
            program.On(() => Sheet.IsOpen = false);
        }
    }

    [CorpusFact]
    public void Settings_About_card_opens_the_same_sheet_naming_its_key_and_OK_closes_it()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                var settings = program.Get<VP_ViewModel_Settings_L>();
                Assert.Equal("Show keys and gestures (F1)", settings.KeysHint);

                settings.OpenKeysCommand.Execute(null);
                Assert.True(Sheet.IsOpen);

                // Pressed again it stays: the button shows, it does not turn over.
                settings.OpenKeysCommand.Execute(null);
                Assert.True(Sheet.IsOpen);

                Sheet.CloseCommand.Execute(null);
                Assert.False(Sheet.IsOpen);
            });
        }
        finally
        {
            program.On(() => Sheet.IsOpen = false);
        }
    }

    [CorpusFact]
    public void It_names_the_menus_keys_the_steppers_the_drags_and_its_own_key()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.Equal("Menu of the item, folder or page under the pointer; put back what is carried", Does("RMB", "Moving items"));
            Assert.Equal("Menu of what is selected", Does("Menu key / Shift + F10", "Moving items"));

            // The right click has no chords: none is named.
            Assert.Null(Does("Ctrl + Shift + RMB", "Moving items"));
            Assert.Null(Does("Alt + RMB", "Moving items"));
            Assert.Equal("On a page in the inventory's list: show it in pane II", Does("Ctrl + LMB", "Getting about"));
            Assert.Equal("Every point: a stat, a skill, the level, points to spend", Does("Ctrl + Shift + LMB", "Character points"));
            Assert.Equal("Socket a gem, a rune or a jewel", Does("Drag onto an item", "Moving items"));
            Assert.Equal("This sheet", Does("F1", "Getting about"));

            // Every line has a key and what it does; no key is said twice under one heading.
            foreach (var group in Sheet.Left.Concat(Sheet.Right))
            {
                Assert.NotEmpty(group.Keys);
                Assert.All(group.Keys, key => Assert.True(key.Keys.Length > 0 && key.Does.Length > 0, group.Title));
                Assert.Equal(group.Keys.Count, group.Keys.Select(key => key.Keys).Distinct().Count());
            }
        });
    }
}
