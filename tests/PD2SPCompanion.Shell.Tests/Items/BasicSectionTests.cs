using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// The Edit page's Basic section through its own editor (BasicEditor), on the worn rare
/// amulet: the item level raised one step by the + and one more with Ctrl - ten at a time elsewhere, but above
/// the least its affixes need Ctrl is a plain step (the floor itself is item-level-floor's to prove) - the
/// picture sent to its last with Shift, each kept as it is made, and the file read back.
/// <para>
/// What the editor decides, asked as the view asks: it is the view that turns Ctrl on a stepper into Leap and
/// Shift into Jump. That the keys held with a click reach the steppers is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BasicSectionTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private BasicEditor Basic => program.Get<VP_ViewModel_Inv_R>().Basic;

    private ItemTileViewModel Amulet => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;

    [CorpusFact]
    public void The_item_level_steps_by_one_and_by_Ctrl_the_picture_jumps_to_its_last_and_the_file_holds_both()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        program.On(() =>
        {
            Assert.Equal("Beast Necklace", Amulet.Name);
            Assert.Equal(81, Amulet.Item.ItemLevel);
            Assert.Equal(1, Amulet.Item.GraphicId);   // the second picture, counted from nought

            Session.SelectedItem = Amulet;
            Assert.True(Basic.HasItemLevel && Basic.HasGraphic);
            Assert.Equal("81", Basic.ItemLevel);
            Assert.Equal("2", Basic.Graphic);

            // Its affixes need less than its 81: toward 99, Ctrl has nothing to land on.
            Assert.True(Basic.AffixFloor < 81);
            Assert.Equal("Shift + LMB: max value", Basic.RaiseItemLevelHint);

            Basic.RaiseItemLevelCommand.Execute(null);
            Assert.Equal(82, Amulet.Item.ItemLevel);

            // Ctrl + click.
            Basic.Leap("itemlevel", up: true);
            Assert.Equal(83, Amulet.Item.ItemLevel);
            Assert.Equal("83", Basic.ItemLevel);

            // Shift + click on the picture's +: its last.
            Assert.Equal(3, Basic.GraphicMaximum);
            Basic.Jump("graphic", up: true);
            Assert.Equal(2, Amulet.Item.GraphicId);
            Assert.False(Basic.CanRaiseGraphic);

            Assert.True(program.Get<AutoSave>().Flush(), "not written");
            Assert.Equal(Session.SavedBytes, File.ReadAllBytes(program.SaveOf(Program.Open)));

            // Read off the disk again.
            program.Main.Home.LoadCommand.Execute(null);
        });

        Assert.True(program.Until(() => Session.IsLoaded && !program.Main.Home.IsLoading && Session.Status == "Opened PhySpearZon.", 15000), "not read again");
        program.On(() =>
        {
            Assert.Equal(83, Amulet.Item.ItemLevel);
            Assert.Equal(2, Amulet.Item.GraphicId);
        });
    }

    [CorpusFact]
    public void A_name_typed_personalizes_the_item_heads_its_tooltip_and_none_takes_it_off_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Casual");
        program.On(() =>
        {
            Session.SelectedItem = Amulet;
            Assert.True(Basic.HasPersonalName);
            Assert.Equal("", Basic.PersonalName);
            Assert.False(Amulet.Item.IsPersonalized);
            Assert.Equal("Beast Necklace", Amulet.Title);

            // One letter is no name: said under the field, and nothing written while it stands.
            Basic.PersonalName = "B";
            Assert.True(Basic.PersonalNameIsBad);
            Assert.Equal("Require 2 to 15 characters.", Basic.PersonalNameNote);
            Assert.False(Amulet.Item.IsPersonalized);

            // A name: on the item at once, and its tooltip is headed with whose it is, as the game heads it.
            Basic.PersonalName = "Bob";
            Assert.False(Basic.PersonalNameIsBad);
            Assert.True(Amulet.Item.IsPersonalized);
            Assert.Equal("Bob", Amulet.Item.PlayerName);
            Assert.Equal("Bob's Beast Necklace", Amulet.Title);
            Assert.Contains("Bob's Beast Necklace", PD2SPCompanion.Core.Items.ItemLines.TextOf(Amulet.Item));

            // Written, and read back off the bytes: the name is the save's.
            Assert.True(program.Get<AutoSave>().Flush(), "not written");
            var read = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Program.Open)));
            Assert.Contains(read.PlayerItemList.Items, item => item.IsPersonalized && item.PlayerName == "Bob");

            // None: the name is off again.
            Basic.PersonalName = "";
            Assert.False(Amulet.Item.IsPersonalized);
            Assert.Equal("Beast Necklace", Amulet.Title);
        });
    }
}
