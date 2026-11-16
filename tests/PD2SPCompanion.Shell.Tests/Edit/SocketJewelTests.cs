using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The sockets' second round. The socket guard: on, a rune let go of on an item with an
/// empty socket is not socketed and the strip says so; off again, it is. And a jewel in a socket opened from the
/// Sockets section into a layer of its own - the Edit page again, over the jewel, under the way back to the item
/// that holds it: a number changed there is written to the jewel inside the item, and the way back shows the
/// item again. A rune has no such button.
/// <para>
/// What the pages decide, asked as the view asks (the guard's command, the drop, the section's OpenJewel, the
/// layer's own rows). That the guard's button and "Edit jewel" are there to press, and that the layer is drawn
/// over the page - the view makes it, markup cannot hold a control inside itself - is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SocketJewelTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    [CorpusFact]
    public void The_socket_guard_refuses_a_drop_in_words_and_off_again_the_same_drop_sockets()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            bool was = Page.SocketGuard;
            try
            {
                Page.SocketGuard = false;
                program.Panes("Personal Stash", "Inventory");
                var cap = program.GivePlain("cap", 1);
                Assert.StartsWith("Socket guard off", Page.SocketGuardHint);

                Page.ToggleSocketGuardCommand.Execute(null);
                Assert.Equal("Socket guard on.", Session.Status);
                Assert.StartsWith("Socket guard on", Page.SocketGuardHint);

                Assert.True(Page.BeginCarry(program.Tile("Hel Rune", "Inventory")));
                Assert.False(Page.WouldSocket(program.TileOf(cap)));
                Page.DropCarried(null, pack: false, onItem: program.TileOf(cap));
                Assert.Equal("Socket guard on. Put back.", Session.Status);
                Assert.Equal(0, cap.Filled());
                Assert.True(program.Holds("Hel Rune", "Inventory"));
                Assert.False(Session.IsHolding);

                Page.ToggleSocketGuardCommand.Execute(null);
                Assert.Equal("Socket guard off.", Session.Status);

                Assert.True(Page.BeginCarry(program.Tile("Hel Rune", "Inventory")));
                Assert.True(Page.WouldSocket(program.TileOf(cap)));
                Page.DropCarried(null, pack: false, onItem: program.TileOf(cap));
                Assert.Equal("Hel Rune socketed into Cap.", Session.Status);
                Assert.Equal(1, cap.Filled());
            }
            finally
            {
                Page.SocketGuard = was;
            }
        });
    }

    [CorpusFact]
    public void Of_four_filled_sockets_only_the_jewels_row_opens_and_it_opens_the_jewel_under_the_way_back()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            program.Select("Stone Barb");
            Assert.Equal(4, Edit.Sockets.Rows.Count(row => row.IsFilled));
            Assert.True(Edit.Sockets.OpensJewels);

            var jewels = Edit.Sockets.Rows.Where(row => row.IsJewel).ToList();
            Assert.Single(jewels);
            Assert.Equal("Ruby Jewel of Envy", jewels[0].Name);

            // A rune's row asked the same opens nothing.
            Edit.Sockets.OpenJewelCommand.Execute(Edit.Sockets.Rows.First(row => row.IsFilled && !row.IsJewel));
            Assert.Null(Edit.Nested);

            Edit.Sockets.OpenJewelCommand.Execute(jewels[0]);
            try
            {
                Assert.NotNull(Edit.Nested);
                Assert.Equal("Ruby Jewel of Envy", Edit.Nested!.Session.SelectedItem?.Name);
                Assert.Equal("Ruby Jewel of Envy", Edit.Nested.Summary?.Name);
                Assert.Equal("Back to Stone Barb", Edit.NestedBack);

                // No layer under the layer: a jewel holds nothing.
                Assert.False(Edit.Nested.OpensSocketed);
            }
            finally
            {
                Edit.CloseNestedCommand.Execute(null);
            }

            Assert.Null(Edit.Nested);
            Assert.Equal("Stone Barb", Edit.Summary?.Name);

            program.Deselect();
        });
    }

    [CorpusFact]
    public void A_number_changed_in_the_layer_is_written_to_the_jewel_in_its_socket_and_the_layer_stands()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item barb = null!, jewel = null!;
        program.On(() =>
        {
            barb = program.Select("Stone Barb");
            jewel = barb.SocketedItems.First(gem => Edit.Sockets.Rows.Any(row => row.IsJewel && ReferenceEquals(row.Gem, gem)));
            Assert.Equal(38, jewel.StatOf("item_maxdamage_percent"));

            Edit.Sockets.OpenJewelCommand.Execute(Edit.Sockets.Rows.First(row => row.IsJewel));
            Edit.Nested!.Row("item_maxdamage_percent").Value = "40";
        });

        Assert.True(program.Until(() => jewel.StatOf("item_maxdamage_percent") == 40), "the jewel's number was not written");
        program.On(() =>
        {
            Assert.Equal("Edited Ruby Jewel of Envy.", Session.Status);
            // Its other half its own: individual editing on all occasions.
            Assert.Equal(38, jewel.StatOf("item_mindamage_percent"));
            Assert.Contains(jewel, barb.SocketedItems);

            // The layer still stands after the write, over the same jewel.
            Assert.NotNull(Edit.Nested);
            Assert.Equal("Ruby Jewel of Envy", Edit.Nested!.Session.SelectedItem?.Name);
            Assert.Equal("Back to Stone Barb", Edit.NestedBack);

            Edit.CloseNestedCommand.Execute(null);
            Assert.Null(Edit.Nested);
            Assert.Equal("Stone Barb", Session.SelectedItem?.Name);
        });
    }
}
