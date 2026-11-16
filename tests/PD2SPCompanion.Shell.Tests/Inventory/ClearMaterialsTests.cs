using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The materials tab's bin: every count at once, so asked by a second press. The first press only
/// asks; the second empties the tab and nothing else of the shared stash, the file read back agreeing; the undo brings
/// it back. Asked through the page's command, and read back from the file. That the bin shows only with the tab on a
/// pane is a window's to show.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ClearMaterialsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private SharedStashSession Stash => program.Get<SharedStashSession>();

    private string StashFile => Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile);

    private static int El => Materials.SlotFor("r01s") ?? throw new InvalidOperationException("the tab has no El Rune");

    private static int Rejuvenation => Materials.SlotFor("rvl") ?? throw new InvalidOperationException("the tab has no rejuvenation");

    private int InTab() => Enumerable.Range(1, Materials.Every).Sum(Stash.MaterialCount);

    /// <summary>The tab on a pane, with at least two kinds in it.</summary>
    private void WithSomethingInTheTab()
    {
        program.Panes("Inventory", "Materials");
        Assert.Null(Stash.AddMaterial(El, 3));
        Assert.Null(Stash.AddMaterial(Rejuvenation, 2));
        Assert.True(program.Saved(), "what was put in the tab was not saved");
    }

    [CorpusFact]
    public void The_first_press_asks_and_changes_nothing_the_second_empties_the_tab_and_only_the_tab()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            WithSomethingInTheTab();
            int held = InTab();
            int gold = Stash.Gold;
            var pages = Stash.Pages.Select(page => page.Items.Count).ToList();
            byte[] written = File.ReadAllBytes(StashFile);
            Assert.True(Page.CanClearMaterials);
            Assert.False(Page.ClearMaterialsArmed);
            Assert.Equal("Clear materials tab", Page.ClearMaterialsHint);

            Page.ClearMaterialsCommand.Execute(null);

            Assert.True(Page.ClearMaterialsArmed);
            Assert.Equal("Clear materials tab? Click red tick again to confirm.", Session.Status);
            Assert.StartsWith("Confirm clearing", Page.ClearMaterialsHint);
            Assert.Equal(held, InTab());
            program.Saved();
            Assert.Equal(written, File.ReadAllBytes(StashFile));

            Page.ClearMaterialsCommand.Execute(null);

            Assert.False(Page.ClearMaterialsArmed);
            Assert.Equal($"Materials tab cleared: {held:N0}.", Session.Status);
            Assert.Equal(0, InTab());
            Assert.False(Page.CanClearMaterials);

            // Only the tab, and the file says the same.
            Assert.Equal(gold, Stash.Gold);
            Assert.Equal(pages, Stash.Pages.Select(page => page.Items.Count));
            Assert.True(program.Saved(), "the cleared tab was not saved");
            var read = SharedStash.Read(StashFile);
            Assert.All(Enumerable.Range(1, Materials.Every), slot => Assert.Equal(0, read.MaterialCount(slot)));
            Assert.Equal(gold, (int)read.Gold);
        });
    }

    [CorpusFact]
    public void A_second_clear_of_an_empty_tab_says_so()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            WithSomethingInTheTab();
            Page.ClearMaterialsCommand.Execute(null);
            Page.ClearMaterialsCommand.Execute(null);
            Assert.Equal(0, InTab());

            Page.ClearMaterialsCommand.Execute(null);
            Assert.True(Page.ClearMaterialsArmed);
            Page.ClearMaterialsCommand.Execute(null);
            Assert.Equal("Materials tab already empty.", Session.Status);
            Assert.False(Page.ClearMaterialsArmed);
        });
    }

    [CorpusFact]
    public void While_the_game_runs_the_bin_leaves_the_file_alone()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(WithSomethingInTheTab);
        byte[] written = File.ReadAllBytes(StashFile);

        File.WriteAllText(program.GameFlag, "running");
        try
        {
            Assert.True(program.Until(() => program.Main.Sync?.IsGameRunning == true && !Session.IsLoaded, 10000), "the game was not seen");
            program.On(() =>
            {
                Page.ClearMaterialsCommand.Execute(null);
                Assert.Equal("Require game closed to clear materials.", Session.Status);
                Assert.False(Page.ClearMaterialsArmed);

                Page.ClearMaterialsCommand.Execute(null);
                Assert.Equal("Require game closed to clear materials.", Session.Status);
                Assert.False(Page.ClearMaterialsArmed);
                program.Saved();
            });

            Assert.Equal(written, File.ReadAllBytes(StashFile));
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => program.Main.Sync?.IsGameRunning != true, 10000);
        }
    }

    [CorpusFact]
    public void The_undo_brings_the_counts_back_into_the_tab_and_the_file()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            WithSomethingInTheTab();
            int held = InTab(), runes = Stash.MaterialCount(El);

            Page.ClearMaterialsCommand.Execute(null);
            Page.ClearMaterialsCommand.Execute(null);
            Assert.True(program.Saved(), "the cleared tab was not saved");

            Assert.Null(program.Get<SessionHistory>().Undo());

            Assert.Equal(held, InTab());
            Assert.Equal(runes, Stash.MaterialCount(El));
            Assert.Equal(runes, SharedStash.Read(StashFile).MaterialCount(El));
        });
    }
}
