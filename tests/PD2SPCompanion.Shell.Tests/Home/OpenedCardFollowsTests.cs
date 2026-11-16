using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// The home page's card is what is open, whichever road opened it: the window search's finds open a character by SwitchTo,
/// which left the card on the one before. And a card's list of backups is that card's only while it is open: left behind,
/// another character's failed load offered its copies, and Settings counted them under another's name.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class OpenedCardFollowsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;

    private void Open(string name)
    {
        program.On(() => HomePage.SwitchTo(program.SaveOf(name)));
        Assert.True(program.Until(() => Session.IsLoaded && !HomePage.IsLoading && Session.SavePath == program.SaveOf(name), 15000), $"{name} was not opened");
    }

    [CorpusFact]
    public void Opening_another_character_by_the_searchs_road_moves_the_card_to_it()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() => Assert.Equal(program.SaveOf(Program.Open), HomePage.Selected?.Path));

            Open(Program.Other);

            program.On(() =>
            {
                Assert.Equal(program.SaveOf(Program.Other), HomePage.Selected?.Path);
                Assert.Equal(Session.SavePath, HomePage.Selected?.Path);
            });
        }
        finally
        {
            Open(Program.Open);
        }
    }

    [CorpusFact]
    public void A_character_that_does_not_open_has_the_card_too()
    {
        if (!program.Ready(changes: true))
            return;

        string path = program.SaveOf(Program.Other);
        byte[] bytes = File.ReadAllBytes(path);
        for (int at = bytes.Length / 2; at < bytes.Length / 2 + 64; at++)
            bytes[at] = 0xFF;
        File.WriteAllBytes(path, bytes);

        program.On(() => HomePage.SwitchTo(path));
        Assert.True(program.Until(() => HomePage.LoadFailed && !HomePage.IsLoading, 15000), "the load did not fail");
        program.On(() => Assert.Equal(path, HomePage.Selected?.Path));
    }

    [CorpusFact]
    public void A_cards_list_of_backups_is_let_go_of_as_it_closes_and_as_another_character_opens()
    {
        if (!program.Ready(changes: true))
            return;

        string open = program.SaveOf(Program.Open), other = program.SaveOf(Program.Other);

        // A copy of the other's, whose folder is the run's own: a list of it can be told from any other.
        string copy = PD2SPCompanion.Core.Saves.SaveWriter.Backup(other);
        try
        {
            program.On(() => HomePage.CloseCommand.Execute(null));
            Assert.True(program.Until(() => !Session.IsLoaded && HomePage.IsBrowsable), "the list of characters is not shown");

            // The other's card, whichever copies its save has: asked for and closed.
            program.On(() =>
            {
                var card = HomePage.Characters.First(each => each.Path == other);
                HomePage.ShowBackupsOf(card);
                Assert.True(HomePage.ShowBackups);
                Assert.Contains(HomePage.Backups, row => row.Path == copy);
                HomePage.CloseBackupPickerCommand.Execute(null);
            });

            // Read again as the fade ends: nothing open, so no copies.
            Assert.True(program.Until(() => !HomePage.IsBackupPickerPresent && HomePage.Backups.All(row => row.Path != copy)), "the card's list stayed");

            // Opened by the card's list being open: the open character's copies, not the card's.
            program.On(() => HomePage.ShowBackupsOf(HomePage.Characters.First(each => each.Path == other)));
            program.On(() => HomePage.SwitchTo(open));
            Assert.True(program.Until(() => Session.IsLoaded && !HomePage.IsLoading && Session.SavePath == open, 15000), "not opened");
            program.On(() =>
            {
                var theirs = PD2SPCompanion.Core.Saves.SaveWriter.BackupsOf(other).Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.Contains(copy, theirs);
                Assert.DoesNotContain(HomePage.Backups, row => theirs.Contains(row.Path));
            });
        }
        finally
        {
            program.On(() => { if (HomePage.ShowBackups) HomePage.CloseBackupPickerCommand.Execute(null); });
            if (File.Exists(copy))
                File.Delete(copy);
        }
    }
}
