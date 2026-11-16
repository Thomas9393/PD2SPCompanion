using System.Text.RegularExpressions;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// An item out:
/// Ctrl + C, the game's own key over an item, copies what is selected as the text the game copies - with a key
/// of the program's own that holds the item whole - from the inventory and from the vault; several selected,
/// one after another; nothing selected, said and nothing copied. The Forge's Paste from game makes the one
/// copied again, and says nothing of lines left out: there are none.
/// <para>
/// What the pages decide: the text they hand the view for the clipboard (CopyText), what they say on their
/// strips, and the item the Forge makes of that text. How an item is written as the game's text is the Core's
/// (GameClipboardTests). That Ctrl + C reaches the page with the focus, and that the text reaches the
/// window's clipboard, are the UI cases'.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CopyItemTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Other = "Entropy Gyre", Charm = "Small Charm of Fortune";

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private ItemTileViewModel InStash(string name) =>
        Page.Sections.First(section => section.Pages is not null)
            .Pages!.First(page => page.Label == "Personal Stash").Grid!.Items.First(tile => tile.Name == name);

    /// <summary>Done on the program's thread, and what it selected on the inventory page let go of after.</summary>
    private void Selecting(Action work) => program.On(() =>
    {
        try
        {
            work();
        }
        finally
        {
            Page.ClearSelection();
        }
    });

    /// <summary>How many items a copied text holds: each carries the program's own key once.</summary>
    private static int Objects(string text) => Regex.Matches(text, "\"pd2spc\"").Count;

    private static IReadOnlyList<ItemTileViewModel> Cards(VP_ViewModel_Vault_L vault)
    {
        foreach (var row in vault.PageRows.ToList())
            _ = row.Pages;

        return vault.Cards;
    }

    [CorpusFact]
    public void With_nothing_selected_it_says_so_and_hands_the_clipboard_nothing()
    {
        if (!program.Ready())
            return;

        Selecting(() =>
        {
            Page.ClearSelection();

            Assert.Null(Page.CopyText());
            Assert.Equal("No item selected.", Main.Session.Status);
        });
    }

    [CorpusFact]
    public void One_item_selected_is_copied_in_the_games_shape_with_the_programs_own_key()
    {
        if (!program.Ready())
            return;

        Selecting(() =>
        {
            Page.ClickTileCommand.Execute(InStash(Ring));
            string? text = Page.CopyText();

            Assert.Equal($"{Ring} copied.", Main.Session.Status);
            Assert.NotNull(text);
            Assert.Equal(1, Objects(text));
            Assert.Matches("\"quality\":\\s*\"Magic\"", text);
            Assert.Matches("\"type\":\\s*\"Ring\"", text);
            Assert.Contains("\"stat_id\"", text);

            // Nothing is changed: the ring is where it was, still the selection.
            Assert.Equal(Ring, Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void Several_selected_are_copied_one_after_another()
    {
        if (!program.Ready())
            return;

        Selecting(() =>
        {
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(InStash(Ring), true);
            Page.SetPicked(InStash(Other), true);
            Page.EndPick();

            string? text = Page.CopyText();

            Assert.Equal("2 items copied.", Main.Session.Status);
            Assert.NotNull(text);
            Assert.Equal(2, Objects(text));
            Assert.Equal(2, Page.PickedCount);
        });
    }

    [CorpusFact]
    public void The_Forge_makes_the_one_copied_again_whole_with_no_word_of_lines_left_out()
    {
        if (!program.Ready())
            return;

        string? text = null;
        List<string> lines = [];
        Selecting(() =>
        {
            var ring = InStash(Ring);
            Page.ClickTileCommand.Execute(ring);
            text = Page.CopyText();
            lines = [.. ring.Summed.Select(line => line.Text)];
        });
        Assert.NotNull(text);

        using var bench = new ForgeBench(program);
        bench.Copied = text;
        program.On(() => _ = bench.Forge.Peek());
        program.On(() =>
        {
            Assert.Equal($"Paste {Ring}", bench.Forge.PasteTip);
            bench.Forge.PasteCopiedCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Forge.HasDraft), "nothing was pasted");
        program.On(() =>
        {
            Assert.Equal($"{Ring} pasted.", bench.Session.RightMessages.Text);
            Assert.Equal(Ring, bench.Forge.Draft!.Name);
            Assert.Equal("Rings", bench.Forge.Type.Shown);
            Assert.Equal("Magic", bench.Forge.Rarity.Shown);

            // Whole: every line the ring has, and no other.
            Assert.Equal(lines, bench.Lines());
        });
    }

    [CorpusFact]
    public void A_vault_item_is_copied_the_same_way_said_on_the_vaults_strip()
    {
        if (!program.Ready())
            return;

        var vault = Main.Vault!;
        try
        {
            program.On(() =>
            {
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                vault.Show();
            });
            Assert.True(program.Until(() => vault.Places.Any(place => place.Name == "Small")), "the vault's folders were not listed");

            program.On(() =>
            {
                // Nothing selected there either.
                Assert.Null(vault.CopyText());
                Assert.Equal("No item selected.", vault.Notice);

                vault.GoToPlace(vault.Places.First(place => place.Name == "Small"));
            });
            Assert.True(program.Until(() => Cards(vault).Any(card => card.Name == Charm)), "the charm's card was not listed");

            program.On(() =>
            {
                vault.ClickTileCommand.Execute(Cards(vault).First(card => card.Name == Charm));
                string? text = vault.CopyText();

                Assert.Equal($"{Charm} copied.", vault.Notice);
                Assert.NotNull(text);
                Assert.Equal(1, Objects(text));
                Assert.Matches("\"type\":\\s*\"Small Charm\"", text);
            });
        }
        finally
        {
            program.On(() =>
            {
                vault.ClearSelection();
                vault.GoTo(null);
                Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            });
        }
    }
}
