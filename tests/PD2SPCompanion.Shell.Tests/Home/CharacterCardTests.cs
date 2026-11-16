using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// The character card's tools:
/// a dead hardcore character revived; a character cloned
/// through the name picker; a character deleted, asked first, with its files and its backups. Clone and
/// delete act on the card chosen in the list, not on an open character.
/// <para>
/// What the home page decides, asked of it as the card's buttons ask and read back from the files in the
/// run's save folder - on "Grave", a dead hardcore Barbarian made for each test. That the buttons are drawn
/// and their clicks arrive is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CharacterCardTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;

    private string Saves => Path.Combine(program.Folder, "saves");

    /// <summary>Grave made, and its card chosen in the list with nothing open - as a player finds it on the home page.</summary>
    private void ChooseGrave()
    {
        program.On(() =>
        {
            Made.Character(program, "Grave", "Barbarian", 20, hardcore: true, dead: true);
            // Close: the list of characters is read again.
            HomePage.CloseCommand.Execute(null);
        });
        Assert.True(program.Until(() => !Session.IsLoaded && HomePage.Characters.Any(card => card.Name == "Grave")), "Grave is not in the list");
        program.On(() => HomePage.SelectedIndex = HomePage.Characters.ToList().FindIndex(card => card.Name == "Grave"));
    }

    private void Leave()
    {
        Made.Gone(program, "Grave", "Gravecopy");

        // Opening a hardcore character opened the hardcore vault, and the settings kept its name.
        program.On(() => program.Get<EditorSettings>().VaultNameHardcore = "");
    }

    [CorpusFact]
    public void A_dead_hardcore_character_is_revived_and_the_button_goes()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            ChooseGrave();
            program.On(() =>
            {
                Assert.True(HomePage.Selected!.IsDead, "start: Grave is a dead hardcore character");

                // Offered on the open character only.
                Assert.False(HomePage.CanRevive);
                HomePage.LoadCommand.Execute(null);
            });
            Assert.True(program.Until(() => Session.IsLoaded && Session.CharacterName == "Grave", 15000), "Grave was not opened");

            program.On(() =>
            {
                Assert.True(HomePage.CanRevive);
                HomePage.ReviveCommand.Execute(null);

                Assert.Equal("Revived Grave.", Session.Status);
                Assert.False(SaveSummary.TryRead(program.SaveOf("Grave"))!.IsDead, "the file still says dead");
                Assert.False(HomePage.Selected!.IsDead);
                Assert.False(HomePage.CanRevive);
                Assert.True(Session.IsLoaded);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void Clone_and_delete_are_the_chosen_cards_not_an_open_characters()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.True(Session.IsLoaded);
            Assert.False(HomePage.CanRename);
            Assert.False(HomePage.CanDelete);
        });

        program.On(() => HomePage.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Session.IsLoaded));
        program.On(() =>
        {
            Assert.NotNull(HomePage.Selected);
            Assert.True(HomePage.CanRename);
            Assert.True(HomePage.CanDelete);
        });
    }

    [CorpusFact]
    public void A_character_is_cloned_through_the_name_picker_and_the_original_is_as_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            ChooseGrave();
            program.On(() =>
            {
                byte[] before = File.ReadAllBytes(program.SaveOf("Grave"));

                HomePage.ToggleCloneCommand.Execute(null);
                Assert.True(HomePage.IsRenaming);
                Assert.True(HomePage.IsCloning);
                Assert.Equal("Clone Grave", HomePage.NameTitle);
                Assert.Equal("CLONE", HomePage.NameConfirmLabel);
                Assert.Equal("", HomePage.NewName);

                HomePage.NewName = "Gravecopy";
                HomePage.ConfirmNameCommand.Execute(null);

                Assert.Equal("Cloned Grave as Gravecopy.", Session.Status);
                Assert.False(HomePage.IsRenaming);

                var copy = SaveSummary.TryRead(program.SaveOf("Gravecopy"));
                Assert.NotNull(copy);
                Assert.Equal(("Gravecopy", 20, "Barbarian"), (copy.Name, copy.Level, copy.ClassName));
                Assert.Contains(HomePage.Characters, card => card.Name == "Gravecopy");

                Assert.Equal(before, File.ReadAllBytes(program.SaveOf("Grave")));
                Assert.Equal("Grave", SaveSummary.TryRead(program.SaveOf("Grave"))!.Name);
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void Delete_asks_first_then_nothing_of_the_character_is_kept_not_its_backups()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            ChooseGrave();
            program.On(() =>
            {
                // A copy beside it, which the delete must leave, and a backup of its own, which it must not.
                HomePage.ToggleCloneCommand.Execute(null);
                HomePage.NewName = "Gravecopy";
                HomePage.ConfirmNameCommand.Execute(null);
                HomePage.SelectedIndex = HomePage.Characters.ToList().FindIndex(card => card.Name == "Grave");
                SaveWriter.Backup(program.SaveOf("Grave"));
                string backups = Path.Combine(SaveWriter.BackupRoot(Saves), "Grave");
                Assert.True(Directory.Exists(backups));

                HomePage.DeleteCharacterCommand.Execute(null);
                Assert.True(Main.Confirm.IsAsking);
                Assert.StartsWith("Delete Grave and its backups?", Main.Confirm.Message);
                Assert.Equal("DELETE", Main.Confirm.ConfirmLabel);

                // The no: nothing is deleted.
                Main.Confirm.RejectCommand.Execute(null);
                Assert.True(File.Exists(program.SaveOf("Grave")));

                HomePage.DeleteCharacterCommand.Execute(null);
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.Equal("Deleted Grave.", Session.Status);
                Assert.DoesNotContain(Directory.GetFiles(Saves), file => Path.GetFileNameWithoutExtension(file) == "Grave");
                Assert.False(Directory.Exists(backups), "Grave's backups are still there");
                Assert.True(File.Exists(program.SaveOf("Gravecopy")), "the copy went with it");
                Assert.DoesNotContain(HomePage.Characters, card => card.Name == "Grave");
                Assert.Contains(HomePage.Characters, card => card.Name == "Gravecopy");
            });
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>
    /// The Delete key in the character list worked only sometimes.
    /// Heard on the list alone, Delete was lost whenever the list had not the keys. Now
    /// the page hears it on the window: with the focus nowhere in the list it asks, and from a box of words it does
    /// not.
    /// </summary>
    [CorpusFact]
    public void Delete_asks_wherever_the_focus_was_left_and_not_from_a_box_of_words()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            ChooseGrave();
            program.On(() =>
            {
                var words = new Avalonia.Controls.TextBox { Width = 120 };
                var elsewhere = new Avalonia.Controls.Button { Content = "elsewhere" };
                var page = new PD2SPCompanion.Shell.Views.VP_View_Default_L { DataContext = HomePage };
                var window = new Avalonia.Controls.Window
                {
                    Width = 1200, Height = 900,
                    Content = new Avalonia.Controls.DockPanel { Children = { words, elsewhere, page } },
                };
                window.Show();
                void Pump()
                {
                    for (int i = 0; i < 6; i++)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    }
                }

                void Press() => window.RaiseEvent(new Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Delete,
                });

                try
                {
                    Pump();

                    // From a box of words: the text's key.
                    words.Focus();
                    Pump();
                    Press();
                    Assert.False(Main.Confirm.IsAsking);

                    // The focus on something that is not the list: the question.
                    elsewhere.Focus();
                    Pump();
                    Press();
                    Assert.True(Main.Confirm.IsAsking);
                    Assert.StartsWith("Delete Grave and its backups?", Main.Confirm.Message);
                    Main.Confirm.RejectCommand.Execute(null);
                    Pump();

                    // And again after the question, the focus left where it closed.
                    Press();
                    Assert.True(Main.Confirm.IsAsking);
                    Main.Confirm.RejectCommand.Execute(null);
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            Leave();
        }
    }
}
