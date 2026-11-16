using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A socket's Copy, so that no one need eject an item to copy it (Duplicate, which put the copy out at once,
/// until the clipboard took its place): what
/// is in the socket is held, and pasted on the Items page a copy of it lands as anything that arrives lands - a
/// pane's open container - under a new identifier, and the item keeps its own and stays the one selected. Where
/// the play mode copies, on the character's page: not in Legit, not on the vault's Edit page, not on the Forge.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SocketCopyTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private (int Page8, int Stash, int Inventory) Places =>
        (program.CountIn("Shared Page 8"), program.CountIn("Personal Stash"), program.CountIn("Inventory"));

    [CorpusFact]
    public void A_socketed_gem_copied_and_pasted_is_a_second_of_it_and_the_item_keeps_its_own()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        var before = (Page8: 0, Stash: 0, Inventory: 0);
        program.On(() =>
        {
            program.Panes("Shared Page 8", "Personal Stash");
            before = Places;
            var shell = program.Select("Duriel's Shell");
            Assert.Equal(2, shell.Filled());
            Assert.True(Edit.Sockets.CopiesSocketed);

            var socket = Edit.Sockets.Rows.First(row => row.IsFilled);
            var gem = socket.Gem!;
            Edit.Sockets.CopySocketedCommand.Execute(socket);

            // Held, and nothing made yet; pasted on the Items page, the copy is made.
            Assert.Equal(before, Places);
            program.Main.Inventory!.PasteCommand.Execute(null);

            // Out where a gem turned out of a worn item lands: the first pane's container.
            Assert.Equal((before.Page8 + 1, before.Stash, before.Inventory), Places);
            Assert.Equal(2, shell.Filled());
            Assert.Contains(gem, shell.SocketedItems);
            Assert.Equal("Duriel's Shell", Session.SelectedItem?.Name);
            Assert.False(Edit.HasChanges);

            // A step of the history, as a copy pasted in the inventory is: taken back by the undo.
            program.Get<AutoSave>().Flush();
            program.Main.Home.UndoCommand.Execute(null);
        });

        Assert.True(program.Until(() => program.CountIn("Shared Page 8") == before.Page8),
            program.On(() => $"page 8 holds {program.CountIn("Shared Page 8")}, {program.Main.Home.UndoHint}"));
    }

    [CorpusFact]
    public void Legit_offers_no_copy()
    {
        if (!program.Ready())
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Select("Duriel's Shell");
            Assert.False(Edit.Sockets.CopiesSocketed);
            program.Deselect();
        });
    }
}
