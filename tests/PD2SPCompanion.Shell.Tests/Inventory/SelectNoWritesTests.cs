using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Selecting items edits nothing (the item panel's rows recycled from one item to the next): a
/// weapon, a charm, a body armor and an amulet - different properties, different ranges - chosen in turn, five
/// rounds; after them nothing waits to be saved, the character's file has not been written and Undo has
/// nothing to undo.
/// <para>
/// The pages' half of it, asked as a click on each asks and read back from the Edit page, the history and the
/// file. The fault this was written for was the view's - a number box handed a new value under an old range
/// clamped it and wrote the clamp back - and only real boxes can show it gone: the UI case
/// (units/select-no-writes.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SelectNoWritesTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    [CorpusFact]
    public void Four_items_chosen_in_turn_five_rounds_leave_nothing_to_save_and_nothing_to_undo()
    {
        if (!program.Ready())
            return;

        try
        {
            string save = program.SaveOf(Program.Open);
            var written = File.GetLastWriteTimeUtc(save);
            program.On(() =>
            {
                var history = program.Get<SessionHistory>();
                var edit = program.Get<VP_ViewModel_Inv_R>();
                int steps = history.Rows.Count;

                ItemTileViewModel Worn(string name) => Session.Equipped.First(slot => slot.Item?.Name == name).Item!;

                for (int round = 0; round < 5; round++)
                {
                    Page.ClickWornCommand.Execute(Worn("Stone Barb"));
                    Assert.False(edit.HasChanges, "the weapon selected is a change");

                    Page.ClickTileCommand.Execute(program.Tile("Annihilus", "Inventory"));
                    Assert.False(edit.HasChanges, "the charm selected is a change");

                    Page.ClickWornCommand.Execute(Worn("Duriel's Shell"));
                    Assert.False(edit.HasChanges, "the armor selected is a change");

                    Page.ClickWornCommand.Execute(Worn("Beast Necklace"));
                    Assert.False(edit.HasChanges, "the amulet selected is a change");
                }

                Assert.Equal("Beast Necklace", Session.SelectedItem?.Name);
                Assert.False(program.Get<SaveSet>().HasChanges);
                Assert.True(program.Saved());
                Assert.Equal(steps, history.Rows.Count);
            });

            Assert.Equal(written, File.GetLastWriteTimeUtc(save));
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
