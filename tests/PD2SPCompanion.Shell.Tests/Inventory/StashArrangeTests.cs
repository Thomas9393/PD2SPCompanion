using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A stash page laid out again (the stash only):
/// a Sort menu as the vault's, and a Close gaps button beside it (the
/// vault has none, its items having no places), on the pane that shows the personal stash or a shared
/// page - and on no other. Close gaps and a sort keep every item, lay none over another
/// and begin at the top left; a sort by Name puts the first by name there; Undo gives the cells of before; a
/// locked page says "Page locked." and nothing is written; a shared page sorted leaves the others as they were.
/// The pad's personal stash holds 38 items laid out by hand, with gaps.
/// <para>
/// What the page decides, asked through the pane's own Sort menu (FirstOrder, as the menu's lines ask it) and
/// read back from the containers as the program holds them, and from the character read again off its file.
/// That the menu is drawn on a stash page's header and on no other, and that a line of it reaches the page,
/// are the UI cases' (units/stash-arrange.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class StashArrangeTests(Program program)
{
    private const string Stash = "Personal Stash";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    /// <summary>A line of the first pane's Sort menu, picked.</summary>
    private void Pick(string line) => Page.FirstOrder.Pick(Page.FirstOrder.Options.First(option => option.Label == line));

    /// <summary>Close gaps, the button beside a pane's Sort menu.</summary>
    private void CloseGaps(string pane = "first") => Page.CloseGapsCommand.Execute(pane);

    /// <summary>How far down a container's lowest item reaches, in rows.</summary>
    private int Depth(string container) => program.Grid(container).Items.Max(tile => tile.Item.Y + tile.View.GridHeight);

    /// <summary>
    /// The order the pane's menu keeps is a setting, written as it is picked: a test that picks one puts back
    /// the one there was.
    /// </summary>
    private IDisposable OrderKept()
    {
        var (by, descending) = program.On(() => (Page.FirstOrder.By, Page.FirstOrder.Descending));
        return new Back(() => program.On(() =>
        {
            Page.FirstOrder.By = by;
            Page.FirstOrder.Descending = descending;
        }));
    }

    private sealed class Back(Action back) : IDisposable
    {
        public void Dispose() => back();
    }

    [CorpusFact]
    public void Only_a_stash_page_can_be_laid_out_again()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.False(program.Choice("Inventory").CanArrange);
            Assert.False(program.Choice("Cube").CanArrange);
            Assert.False(program.Choice("Materials").CanArrange);
            Assert.True(program.Choice(Stash).CanArrange);
            Assert.True(program.Choice("Shared Page 2").CanArrange);

            // The menu is orders and their directions, and nothing done to the page as it lies: closing
            // its gaps is the button beside it.
            Assert.Equal(["Type", "Name", "Rarity", "Ascending", "Descending"], Page.FirstOrder.Options.Select(option => option.Label));
            Assert.True(Page.CloseGapsCommand.CanExecute("first"));
        });
    }

    [CorpusFact]
    public void Picked_with_the_inventory_shown_the_menu_moves_nothing()
    {
        if (!program.Ready())
            return;

        using var order = OrderKept();
        program.On(() =>
        {
            string cells = program.Cells("Inventory");
            Session.Status = "";

            CloseGaps();
            Assert.Equal(cells, program.Cells("Inventory"));
            Assert.Equal("", Session.Status);
            Assert.False(program.Get<SaveSet>().HasChanges);
        });
    }

    [CorpusFact]
    public void Close_gaps_closes_the_gaps_and_keeps_every_item()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            Assert.Equal(38, program.Grid(Stash).Items.Count);
            Assert.False(program.Overlaps(Stash));
            string ids = program.Ids(Stash), cells = program.Cells(Stash);
            int depth = Depth(Stash);

            CloseGaps();
            Assert.Equal("Personal Stash: gaps closed.", Session.Status);
            Assert.Equal(ids, program.Ids(Stash));
            Assert.False(program.Overlaps(Stash));
            Assert.NotNull(program.At(Stash, 0, 0));
            Assert.True(Depth(Stash) <= depth, $"the page is deeper than it was: {Depth(Stash)} rows, {depth} before");
            Assert.NotEqual(cells, program.Cells(Stash));
        });
    }

    [CorpusFact]
    public void A_sort_by_name_puts_the_first_by_name_at_the_top_left_and_undo_gives_the_cells_of_before()
    {
        if (!program.Ready(changes: true))
            return;

        using var order = OrderKept();
        string tidy = "";
        program.On(() =>
        {
            program.Panes(Stash);
            string ids = program.Ids(Stash);

            CloseGaps();
            Assert.True(program.Saved(), "the tidy was not saved");
            tidy = program.Cells(Stash);

            Pick("Name");
            Assert.Equal("Personal Stash sorted by Name.", Session.Status);
            Assert.Equal(ids, program.Ids(Stash));
            Assert.False(program.Overlaps(Stash));

            // The first as the page orders names: ignoring case, and nothing else.
            string first = program.Grid(Stash).Items.Select(tile => tile.Name).Order(StringComparer.OrdinalIgnoreCase).First();
            Assert.Equal(first, program.At(Stash, 0, 0)?.Name);
            Assert.NotEqual(tidy, program.Cells(Stash));
            Assert.True(program.Saved(), "the sort was not saved");

            // Undo answers why not, or nothing where it is done.
            var history = program.Get<SessionHistory>();
            Assert.True(history.CanUndo);
            Assert.Null(history.Undo());
        });

        Assert.True(program.Until(() => Session.IsLoaded && program.Cells(Stash) == tidy), "Undo did not give the cells of before the sort");
    }

    [CorpusFact]
    public void A_locked_page_is_left_as_it_lies_and_nothing_is_written()
    {
        if (!program.Ready(changes: true))
            return;

        using var order = OrderKept();
        try
        {
            program.On(() =>
            {
                program.Panes(Stash);
                string cells = program.Cells(Stash);

                Page.TogglePageLockCommand.Execute(program.Choice(Stash));
                Assert.True(program.Choice(Stash).IsLocked);

                var kept = (Page.FirstOrder.By, Page.FirstOrder.Descending);
                Pick("Rarity");
                Assert.Equal("Page locked.", Session.Status);
                Assert.Equal(cells, program.Cells(Stash));
                Assert.False(program.Get<SaveSet>().HasChanges);

                // The menu's bullet stays on the order the page was last laid out in.
                Assert.Equal(kept, (Page.FirstOrder.By, Page.FirstOrder.Descending));

                CloseGaps();
                Assert.Equal("Page locked.", Session.Status);
                Assert.Equal(cells, program.Cells(Stash));
                Assert.False(program.Get<SaveSet>().HasChanges);
            });
        }
        finally
        {
            // The lock is a setting.
            program.On(() =>
            {
                if (program.Choice(Stash).IsLocked)
                    Page.TogglePageLockCommand.Execute(program.Choice(Stash));
            });
        }
    }

    [CorpusFact]
    public void A_shared_page_is_laid_out_and_the_other_shared_pages_are_as_they_were()
    {
        if (!program.Ready(changes: true))
            return;

        using var order = OrderKept();
        program.On(() =>
        {
            program.Panes("Shared Page 2");
            string ids = program.Ids("Shared Page 2"), cells = program.Cells("Shared Page 2"), one = program.Cells("Shared Page 1");
            Assert.True(program.Grid("Shared Page 2").Items.Count >= 10);

            Pick("Type");
            Assert.Equal("Shared Page 2 sorted by Type.", Session.Status);
            Assert.Equal(ids, program.Ids("Shared Page 2"));
            Assert.False(program.Overlaps("Shared Page 2"));
            Assert.NotNull(program.At("Shared Page 2", 0, 0));
            Assert.NotEqual(cells, program.Cells("Shared Page 2"));
            Assert.Equal(one, program.Cells("Shared Page 1"));
        });
    }

    [CorpusFact]
    public void A_page_laid_out_again_reads_back_from_its_files_as_it_was_left()
    {
        if (!program.Ready(changes: true))
            return;

        using var order = OrderKept();
        string stash = "", shared = "";
        program.On(() =>
        {
            program.Panes(Stash, "Shared Page 2");
            Pick("Name");
            CloseGaps("second");
            Assert.Equal("Shared Page 2: gaps closed.", Session.Status);
            stash = program.Cells(Stash);
            shared = program.Cells("Shared Page 2");
            Assert.True(program.Saved(), "the pages were not saved");

            // The character and its shared stash read again off their files.
            Main.Home.SwitchTo(program.SaveOf(Program.Open));
        });

        Assert.True(program.Until(() => Session.IsLoaded && Page.Sections.Any(section => section.Pages is not null), 15000), "the character was not read again");
        program.On(() =>
        {
            Assert.Equal(stash, program.Cells(Stash));
            Assert.Equal(shared, program.Cells("Shared Page 2"));
        });
    }

    /// <summary>
    /// A file's era turned by the item that left it: the sort refused, and the way back asks nothing of the file, or the
    /// item refused twice would be in no list and the autosave would write that.
    /// </summary>
    [CorpusFact]
    public void A_sort_a_file_refuses_leaves_every_item_in_its_list_and_cell()
    {
        if (!program.Ready(changes: true))
            return;

        using var order = OrderKept();
        program.On(() =>
        {
            var shared = program.Get<SharedStashSession>();
            var first = shared.HeldItems.First();
            int page = PD2SPCompanion.Core.Saves.SharedStash.PageOf(first)!.Value;
            program.Panes($"Shared Page {page}");

            first.Version = "50";
            Assert.Equal("50", shared.ItemEra);
            var before = shared.HeldItems.Select(item => (item, item.X, item.Y)).ToList();

            Pick("Type");

            Assert.EndsWith("Nothing moved.", Session.Status);
            Assert.True(before.Select(each => each.item).SequenceEqual(shared.HeldItems, ReferenceEqualityComparer.Instance), "the items do not stand as they did");
            Assert.Equal(before.Select(each => (each.X, each.Y)), shared.HeldItems.Select(item => (item.X, item.Y)));
        });
        program.PanesAsFound();
    }
}
