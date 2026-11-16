using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The materials tab's count, set on the Edit page with nothing to press after (keeping at once):
/// the tab's El Rune chosen - the pad's shared stash holds eleven - a count put in the Quantity box is the
/// tab's count at once and the file's once the autosave has written, the tab's tile says it, the stepper's
/// plus takes it one further, and Undo takes the last change back.
/// <para>
/// What the Edit page and the shared stash decide, asked as the box and the stepper ask (the Quantity text,
/// its raise command) and read back from the tab, its tile and the shared stash's file. That digits typed in
/// the box with Enter, and a press of the stepper, reach them is the UI case's (units/materials-count.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MaterialsCountTests(Program program)
{
    /// <summary>The El Rune's slot of the tab.</summary>
    private static int El => Materials.SlotFor("r01s") ?? throw new InvalidOperationException("the tab has no El Rune");

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private BasicEditor Basic => program.Get<VP_ViewModel_Inv_R>().Basic;

    private ItemTileViewModel TabRune => Page.Materials.TileFor(El)!;

    /// <summary>The El Runes the shared stash's file holds in its tab.</summary>
    private int InFile()
    {
        Page.Stash.UseMyLayout();
        return SharedStash.Read(Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile)).MaterialCount(El);
    }

    /// <summary>The tab in the second pane, its El Rune chosen: the Edit page is on it.</summary>
    private void ChooseTheRune()
    {
        program.Panes("Inventory", "Materials");
        Page.ClickTileCommand.Execute(TabRune);
        Assert.Same(TabRune, Session.SelectedItem);
    }

    [CorpusFact]
    public void The_tabs_tile_and_the_Edit_pages_Quantity_box_say_what_the_file_holds()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Assert.Equal(11, InFile());
                ChooseTheRune();

                Assert.Equal("El Rune", TabRune.Name);
                Assert.Equal(11, TabRune.Tally);
                Assert.Equal("11", TabRune.QuantityText);

                Assert.True(Basic.HasQuantity);
                Assert.True(Basic.EditsQuantity);
                Assert.Equal("11", Basic.Quantity);
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_count_typed_is_the_tabs_with_nothing_pressed_after_and_the_tile_says_it()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            ChooseTheRune();

            Basic.Quantity = "25";
            Assert.Equal(25, Page.Stash.MaterialCount(El));
            Assert.Equal(25, TabRune.Tally);
            Assert.Equal("25", TabRune.QuantityText);

            // The tab was drawn again: the tile looked at is the one now drawn, and the box still says its count.
            Assert.Same(TabRune, Session.SelectedItem);
            Assert.Equal("25", Basic.Quantity);

            Assert.True(program.Saved(), "the count was not saved");
            Assert.Equal(25, InFile());
        });
    }

    [CorpusFact]
    public void The_steppers_plus_takes_it_one_further_and_Undo_takes_the_last_change_back()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            ChooseTheRune();
            Basic.Quantity = "25";
            Assert.True(program.Saved(), "the count typed was not saved");

            Assert.True(Basic.CanRaiseQuantity);
            Basic.RaiseQuantityCommand.Execute(null);
            Assert.Equal("26", Basic.Quantity);
            Assert.Equal(26, Page.Stash.MaterialCount(El));
            Assert.True(program.Saved(), "the count stepped was not saved");
            Assert.Equal(26, InFile());

            var history = program.Get<SessionHistory>();
            Assert.True(history.CanUndo);
            Assert.Null(history.Undo());
        });

        Assert.True(program.Until(() => Session.IsLoaded && Page.Stash.MaterialCount(El) == 25), "Undo did not take the step back");
        program.On(() => Assert.Equal(25, InFile()));
    }
}
