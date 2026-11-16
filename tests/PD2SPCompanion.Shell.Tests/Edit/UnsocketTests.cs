using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A gem turned out of its socket on the Edit page (SocketsEditor; ItemLanding), landing around that item:
/// one by its row's Unsocket, all of them by Unsocket all, each written at once. Where it
/// lands: a worn item is in no grid, so in the first pane's container; an item in a grid, beside it - in the
/// personal stash or on a shared page - whatever the panes show. With nowhere to go it stays in its socket and
/// the page says why, nothing written; a pane then opened on a page with room, it goes there by itself.
/// <para>
/// What the page and the landing rule decide, asked as the view asks (the section's commands, the panes'
/// pickers) and read back from the items and the containers. Nothing of it is the window's alone but that a
/// click on a worn slot shows its item on the Edit page, which one UI case keeps.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class UnsocketTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private SocketViewModel FirstFilled => Edit.Sockets.Rows.First(row => row.IsFilled);

    /// <summary>Items stored loose on the character: the inventory, the cube and the personal stash.</summary>
    private int Loose => program.CountIn("Inventory") + program.CountIn("Cube") + program.CountIn("Personal Stash");

    private (int Page8, int Stash, int Inventory) Places =>
        (program.CountIn("Shared Page 8"), program.CountIn("Personal Stash"), program.CountIn("Inventory"));

    [CorpusFact]
    public void A_gem_turned_out_of_a_worn_item_is_stored_loose_and_Unsocket_all_turns_out_the_rest()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var barb = program.Select("Stone Barb");
            Assert.Equal(4, barb.Filled());
            int loose = Loose;
            Assert.Equal("Unsocket all", Edit.Sockets.UnsocketAllHint);

            Edit.Sockets.UnsocketCommand.Execute(FirstFilled);
            Assert.Equal(3, barb.Filled());
            Assert.Equal(4, barb.TotalNumberOfSockets);
            Assert.Equal(loose + 1, Loose);
            Assert.Equal("Sockets  (3 / 4)", Edit.Sockets.Header);
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);

            Assert.True(Edit.Sockets.CanUnsocketAll);
            Edit.Sockets.UnsocketAllCommand.Execute(null);
            Assert.Equal(0, barb.Filled());
            Assert.Equal(4, barb.TotalNumberOfSockets);
            Assert.Equal(loose + 4, Loose);
            Assert.False(Edit.Sockets.CanUnsocketAll);
            Assert.False(Edit.HasChanges);
        });
    }

    [CorpusFact]
    public void From_a_worn_item_the_gem_lands_in_the_first_panes_container()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Shared Page 8", "Personal Stash");
            var before = Places;
            var shell = program.Select("Duriel's Shell");
            Assert.Equal(2, shell.Filled());

            Edit.Sockets.UnsocketCommand.Execute(FirstFilled);

            Assert.Equal((before.Page8 + 1, before.Stash, before.Inventory), Places);
            Assert.Equal(1, shell.Filled());
            Assert.Equal(2, shell.TotalNumberOfSockets);
        });
    }

    [CorpusFact]
    public void From_an_item_in_the_stash_the_gem_lands_beside_it_not_on_the_first_panes_page()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Shared Page 8", "Personal Stash");
            var before = Places;
            var bow = program.Select("Witchwild String", "Personal Stash");

            Edit.Sockets.UnsocketCommand.Execute(FirstFilled);

            Assert.Equal((before.Page8, before.Stash + 1, before.Inventory), Places);
            Assert.Equal(2, bow.Filled());
        });
    }

    [CorpusFact]
    public void From_an_item_on_a_shared_page_the_gem_lands_beside_it_there_not_in_the_stash()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash", "Shared Page 8");
            var before = Places;

            // Two Veils of Steel lie on Page 8, and one is empty: the one that holds a rune.
            var veil = program.Choice("Shared Page 8").Grid!.Items.First(tile => tile.Name == "Veil of Steel" && tile.Item.Filled() == 1);
            Session.SelectedItem = veil;
            Assert.Equal("Sockets  (1 / 1)", Edit.Sockets.Header);

            Edit.Sockets.UnsocketCommand.Execute(FirstFilled);

            Assert.Equal((before.Page8 + 1, before.Stash, before.Inventory), Places);
            Assert.Equal(0, veil.Item.Filled());
            Assert.Equal(1, veil.Item.TotalNumberOfSockets);
        });
    }

    [CorpusFact]
    public void With_nowhere_to_go_the_gem_stays_and_the_page_says_why_and_a_pane_opened_on_room_takes_it()
    {
        if (!program.Ready(changes: true))
            return;

        Item shell = null!;
        int page8 = 0;
        program.On(() =>
        {
            program.Panes("Inventory", "Personal Stash");

            // Every free cell of the character's filled with chipped amethysts, as pd2spc fill does.
            var gems = Enumerable.Range(1, 400).Select(n =>
            {
                var gem = ItemForge.Material("gcv", 1, Session.ItemEra!);
                gem.Id = 0x7E580000u + (uint)n;
                return gem;
            }).ToList();
            var filled = program.Get<ItemLanding>().Welcome(gems);
            Assert.True(filled.Placed > 0 && filled.NoRoom.Count > 0, $"the character is full ({filled.Placed} placed)");

            page8 = program.CountIn("Shared Page 8");
            shell = program.Select("Duriel's Shell");
            Edit.Sockets.UnsocketCommand.Execute(FirstFilled);

            Assert.Equal("No free space for it. Make space, or put it back.", Edit.ChangeNote);
            Assert.False(Edit.CanApply);
            Assert.True(Edit.HasChanges);
            Assert.Equal(2, shell.Filled());
            Assert.Equal(page8, program.CountIn("Shared Page 8"));
        });

        // Nothing is written while it waits: asked again after the program has had its turn.
        program.Turned();
        program.On(() =>
        {
            Assert.Equal(2, shell.Filled());
            Assert.True(Edit.HasChanges);

            // The second pane on a page with room: the gem held back goes there by itself.
            program.Section().SecondPage = program.Choice("Shared Page 8");
        });

        Assert.True(program.Until(() => shell.Filled() == 1), "the gem did not leave once a pane showed room");
        program.On(() =>
        {
            Assert.Equal(page8 + 1, program.CountIn("Shared Page 8"));
            Assert.Equal(2, shell.TotalNumberOfSockets);
            Assert.False(Edit.HasChanges);
            Assert.Equal("", Edit.ChangeNote);
        });
    }
}
