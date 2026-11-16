using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The warning's button for items stored on a page no container draws (a jewel back from the game's
/// own save as a loose item on page 0): what is out of sight is counted and said, moved into the first container with
/// room, and said to have nowhere to go where there is none. On Finder, its save written with stray items, and read back
/// from the file once saved.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MoveStraysTests(Program program)
{
    private const byte Nowhere = 0;

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    /// <summary>Finder's save with so many of its stored items put on a page nothing draws, and Finder opened over it.</summary>
    private List<uint> OpenWithStrays(int count)
    {
        var ids = program.On(() =>
        {
            var save = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Program.Other)));
            var strays = save.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Stored).Take(count).ToList();
            Assert.Equal(count, strays.Count);
            foreach (var item in strays)
                item.Page = Nowhere;
            File.WriteAllBytes(program.SaveOf(Program.Other), SaveWriter.Prepared(save));
            Session.UseMyLayout();
            return strays.Select(item => item.Id).ToList();
        });

        Made.Open(program, Program.Other);
        return ids;
    }

    [CorpusFact]
    public void Strays_are_counted_moved_into_view_and_the_file_has_them_there()
    {
        if (!program.Ready(changes: true))
            return;

        var ids = OpenWithStrays(2);
        program.On(() =>
        {
            Assert.True(Page.HasUnknownPages);
            var (page, count) = Assert.Single(Session.UnknownPages);
            Assert.Equal((Nowhere, 2), (page, count));
            Assert.Contains($"2 items on page {Nowhere}", Page.UnknownPagesWarning);

            Page.MoveStraysCommand.Execute(null);

            Assert.Equal("Moved 2 items into view.", Session.Status);
            Assert.Empty(Session.UnknownPages);
            Assert.False(Page.HasUnknownPages);
            Assert.Equal("", Page.UnknownPagesWarning);

            // Each on a page that is drawn, and a tile there.
            Assert.All(ids, id => Assert.Contains(Page.Sections.SelectMany(section => section.Pages ?? []).Where(choice => choice.Grid is not null).SelectMany(choice => choice.Grid!.Items), tile => tile.Item.Id == id));

            Assert.True(program.Saved(), "the move was not saved");
            var read = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Program.Other)));
            Assert.All(read.PlayerItemList.Items.Where(item => ids.Contains(item.Id)), item => Assert.NotEqual(Nowhere, (byte)item.Page));
            Session.UseMyLayout();
        });
    }

    [CorpusFact]
    public void One_stray_is_said_in_the_singular()
    {
        if (!program.Ready(changes: true))
            return;

        OpenWithStrays(1);
        program.On(() =>
        {
            Page.MoveStraysCommand.Execute(null);
            Assert.Equal("Moved 1 item into view.", Session.Status);
            Assert.Empty(Session.UnknownPages);
        });
    }

    [CorpusFact]
    public void With_no_strays_nothing_moves_and_nothing_is_warned_of()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Assert.False(Page.HasUnknownPages);
            Assert.Equal("", Page.UnknownPagesWarning);
        });
    }

    [CorpusFact]
    public void With_every_page_full_the_stray_stays_where_it_is_and_the_words_say_so()
    {
        if (!program.Ready(changes: true))
            return;

        OpenWithStrays(1);
        program.On(() =>
        {
            // Every free cell filled, a potion a cell, through the door that lands items where told.
            foreach (var grid in new[] { "Inventory", "Cube", "Personal Stash" }.Select(name => program.Grid(name)))
            {
                foreach (var cell in grid.Cells.ToList())
                {
                    if (grid.Items.Any(tile => cell.Column >= tile.Item.X && cell.Column < tile.Item.X + tile.View.GridWidth
                                               && cell.Row >= tile.Item.Y && cell.Row < tile.Item.Y + tile.View.GridHeight))
                        continue;

                    var potion = PD2SPCompanion.Core.Items.ItemForge.PlainBase("hp1", 0, Session.ItemEra!).Item;
                    Assert.Null(Session.Adopt(potion, grid.Page, cell.Column, cell.Row));
                }
            }

            Page.MoveStraysCommand.Execute(null);

            Assert.Equal("No free space for 1 item.", Session.Status);
            Assert.Single(Session.UnknownPages);
            Assert.True(Page.HasUnknownPages);
        });
    }
}
