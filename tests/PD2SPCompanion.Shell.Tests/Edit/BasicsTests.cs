using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The Basic section's boxes on the Edit page (BasicEditor). Ethereal on armor moves the defense with it:
/// Duriel's Shell, 177, stores a quarter more made ethereal and goes back the same way.
/// And the item level: the line under the box says the least the affixes need; Ctrl on
/// - or + goes to that floor from the side it lies on and is a plain step from the other; the die rolls
/// evenly among the levels the item could hold, never under the floor and never the level it had. And the
/// alert of an item the game could not hold - a Cap with six sockets - which its tile reads off the item again
/// once the item is put right, not from an answer it kept.
/// <para>
/// What the editor and the tile decide, asked as the view asks (the switch's command, the box's text, Leap for
/// a Ctrl click, the die's command) and read back from the item. That a Ctrl click on a stepper reaches Leap
/// and that the alert is drawn in its corner of the tile are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BasicsTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private BasicEditor Basic => Edit.Basic;

    [CorpusFact]
    public void Ethereal_on_armor_moves_the_defense_with_it_both_ways()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        program.On(() =>
        {
            var shell = program.Select("Duriel's Shell");
            Assert.True(Basic.HasEthereal);
            Assert.False(shell.IsEthereal);
            Assert.Equal("177", Basic.Defense);

            Basic.ToggleEtherealCommand.Execute(null);
            Assert.Equal("221", Basic.Defense);
            Assert.True(shell.IsEthereal);
            Assert.Equal(221, shell.Armor);

            Basic.ToggleEtherealCommand.Execute(null);
            Assert.Equal("177", Basic.Defense);
            Assert.False(shell.IsEthereal);
            Assert.Equal(177, shell.Armor);

            Basic.ToggleEtherealCommand.Execute(null);
            Assert.Equal("221", Basic.Defense);
            Assert.True(shell.IsEthereal);
            Assert.Equal(221, shell.Armor);
            Assert.False(Edit.HasChanges);
            Assert.Contains("Ethereal", string.Join(" ", Edit.Lines()));
        });
    }

    [CorpusFact]
    public void Ctrl_on_the_item_levels_steppers_goes_to_the_least_the_affixes_need_from_the_side_it_lies_on()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var ring = program.Select("Steel Ring of Skill", "Personal Stash");

            // The floor, as the line under the field says it while the level is past it.
            var said = System.Text.RegularExpressions.Regex.Match(Basic.ItemLevelLimits, @"^affixes (\d+)\+ \u00B7 max 99$");
            Assert.True(said.Success, Basic.ItemLevelLimits);
            int floor = int.Parse(said.Groups[1].Value);
            Assert.Equal(floor, Basic.AffixFloor);
            Assert.InRange(floor, 2, ring.ItemLevel - 1);

            Basic.ItemLevel = (floor - 5).ToString();
            Assert.Equal("max 99", Basic.ItemLevelLimits);
            Assert.StartsWith($"Ctrl + LMB: to {floor}, what the affixes need", Basic.RaiseItemLevelHint);
            Basic.Leap("itemlevel", up: true);
            Assert.Equal(floor, ring.ItemLevel);

            Basic.ItemLevel = (floor - 5).ToString();
            Basic.Leap("itemlevel", up: false);
            Assert.Equal(floor - 6, ring.ItemLevel);

            Basic.ItemLevel = "90";
            Assert.StartsWith($"Ctrl + LMB: to {floor}", Basic.LowerItemLevelHint);
            Basic.Leap("itemlevel", up: false);
            Assert.Equal(floor, ring.ItemLevel);

            Basic.ItemLevel = "90";
            Basic.Leap("itemlevel", up: true);
            Assert.Equal(91, ring.ItemLevel);
            Assert.Equal("91", Basic.ItemLevel);
        });
    }

    [CorpusFact]
    public void The_item_levels_die_rolls_a_new_level_each_time_never_under_what_the_affixes_need()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var ring = program.Select("Steel Ring of Skill", "Personal Stash");
            int floor = Basic.AffixFloor!.Value;
            Assert.True(Basic.CanRerollItemLevel);

            for (int roll = 0; roll < 10; roll++)
            {
                int was = ring.ItemLevel;
                Basic.RerollItemLevelCommand.Execute(null);
                Assert.NotEqual(was, ring.ItemLevel);
                Assert.InRange(ring.ItemLevel, floor, 99);
            }

            Assert.Equal($"Reroll within {floor}-99", Basic.ItemLevelRerollHint);
        });
    }

    [CorpusFact]
    public void A_tile_wears_the_alert_of_an_item_the_game_could_not_hold_and_lets_go_of_it_with_the_cause()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            // A cap takes two sockets; a unique helm beside it, as the game makes it.
            var cap = program.GivePlain("cap", 6);
            var crest = program.Give("harlequin", "Harlequin Crest")!;

            Assert.True(program.TileOf(cap).IsImpossible);
            Assert.NotNull(program.TileOf(cap).Marks);
            Assert.False(program.TileOf(crest).IsImpossible);

            program.Main.Session.SelectedItem = program.TileOf(cap);
            Assert.True(Edit.AnyImpossible);

            // Its sockets taken off, as Shift on the stepper's - takes them: the same tile reads the item again.
            var tile = program.TileOf(cap);
            Edit.Sockets.Jump(up: false);
            Assert.Equal(0, cap.TotalNumberOfSockets);
            Assert.False(tile.IsImpossible);
            Assert.False(program.TileOf(cap).IsImpossible);
            Assert.False(Edit.AnyImpossible);
        });
    }
}
