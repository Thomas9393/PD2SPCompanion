using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A socket's Copy pasted, its item on a shared page: the Edit page stays on that item wherever the copy lands. Landed
/// on the character, its rebuild looked for the item shown among the character's own tiles alone and left the page on
/// no item; landed on a shared page, the selection kept a tile no page drew any more.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SharedSocketCopyTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    [CorpusFact]
    public void A_gem_copied_out_of_a_shared_page_item_and_pasted_on_the_character_leaves_the_item_shown()
    {
        if (!program.Ready(changes: true))
            return;

        Pasted(onTheHostsPage: false);
    }

    [CorpusFact]
    public void A_gem_copied_out_of_a_shared_page_item_and_pasted_on_its_page_leaves_the_item_shown_by_its_new_tile()
    {
        if (!program.Ready(changes: true))
            return;

        Pasted(onTheHostsPage: true);
    }

    private void Pasted(bool onTheHostsPage)
    {
        using var free = program.InMode("Free");
        int before = 0;
        StashPageChoice? landing = null;
        program.On(() =>
        {
            var section = program.Section();
            var shared = section.Pages!.Where(page => page.Grid is { Shared: true }).ToList();
            var tile = shared.SelectMany(page => page.Grid!.Items).FirstOrDefault(each => each.Item.SocketedItems.Count > 0);
            Assert.True(tile is not null, "no socketed item on a shared page of the pad");
            var host = tile!.Item;

            // The first pane, where the copy lands: the host's own page, or the character's stash.
            landing = onTheHostsPage ? shared.First(page => page.Grid!.Items.Contains(tile)) : program.Choice("Personal Stash");
            section.SelectedPage = landing;
            section.SecondShown = false;
            before = landing.Grid!.Items.Count;

            Session.SelectedItem = tile;
            Assert.True(Edit.Sockets.CopiesSocketed);
            Edit.Sockets.CopySocketedCommand.Execute(Edit.Sockets.Rows.First(row => row.IsFilled));
            program.Main.Inventory!.PasteCommand.Execute(null);

            Assert.Equal(before + 1, landing.Grid!.Items.Count);
            Assert.Same(host, Session.SelectedItem?.Item);
            Assert.True(Edit.HasItem);

            // Shown by the tile a shared page draws now, not one a rebuild left behind.
            Assert.Contains(Session.SelectedItem, program.Section().Pages!.Where(page => page.Grid is { Shared: true }).SelectMany(page => page.Grid!.Items));

            program.Get<AutoSave>().Flush();
            program.Main.Home.UndoCommand.Execute(null);
        });

        Assert.True(program.Until(() => landing!.Grid!.Items.Count == before),
            program.On(() => $"{landing!.Grid!.Items.Count} on the page pasted on, {before} before, {program.Main.Home.UndoHint}"));
    }
}
