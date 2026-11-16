using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// Reload loses no edit and asks nothing ("Close without saving" and its kin were wording
/// from before the autosave): what waits for the autosave is saved first, as Close and Quit save it, and the
/// file read back holds it.
/// <para>
/// Pressed here before the autosave's pause is up - which the old script, a second away from the button, could
/// not do: it passed without the saving too.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ReloadTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;
    private VP_ViewModel_Char_L Character => program.Get<VP_ViewModel_Char_L>();

    [CorpusFact]
    public void Reload_saves_what_is_waiting_first_and_asks_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        int vitality = 0;
        program.On(() =>
        {
            byte[] before = File.ReadAllBytes(program.SaveOf(Program.Open));
            Assert.True(HomePage.IsReload);
            Assert.Equal("RELOAD", HomePage.LoadLabel);

            // An edit: a point moved on an attribute, whichever way the character can move one.
            var row = Character.Attributes.First(each => each.Stat == "vitality");
            vitality = row.Value;
            if (row.CanRaise)
            {
                row.RaiseCommand.Execute(null);
                vitality++;
            }
            else
            {
                row.LowerCommand.Execute(null);
                vitality--;
            }

            Assert.Equal(vitality, row.Value);
            Assert.True(program.Get<SaveSet>().HasChanges, "nothing waits for the autosave");
            Assert.Equal(before, File.ReadAllBytes(program.SaveOf(Program.Open)));

            // Reload, straight away.
            HomePage.LoadCommand.Execute(null);

            Assert.False(Main.Confirm.IsAsking, "the unsaved-changes question");
            Assert.NotEqual(before, File.ReadAllBytes(program.SaveOf(Program.Open)));
        });

        Assert.True(program.Until(() => Session.IsLoaded && !HomePage.IsLoading && Session.Status == "Opened PhySpearZon.", 15000), "not read again");
        program.On(() => Assert.Equal(vitality, Character.Attributes.First(each => each.Stat == "vitality").Value));
    }
}
