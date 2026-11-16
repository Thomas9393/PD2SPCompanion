using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// An edit on the vault's Edit page is a step of the history whatever came before it.
/// After a save that wrote the vault -
/// an item stored from the character - the history let go of the vault's whole log, the log numbered its next
/// changes from 1 again, under the mark the history had kept, and the edit was no step: Undo took the stored
/// item out of the vault instead of taking back the edit.
/// <para>
/// What the history decides, asked through the pages' own commands and read back from the vault's file.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultEditUndoTests(Program program)
{
    private MainViewModel Main => program.Main;
    private SuperiorEditor Superior => program.VaultEditPage().Superior;
    private int InVault => program.Get<VaultService>().Store!.Count();

    /// <summary>
    /// An edit on a shared page taken back: the stash is made afresh, and the page shows its new item, not the old one, whose
    /// next edit would mark no file and be lost on screen.
    /// </summary>
    [CorpusFact]
    public void An_edit_on_a_shared_page_undone_leaves_the_page_on_the_stashs_own_item()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");

        var stash = program.Get<PD2SPCompanion.Shell.Services.Character.SharedStashSession>();
        Item item = null!;
        string hint = "";
        program.On(() =>
        {
            program.Panes("Shared Page 8");
            var tile = program.Choice("Shared Page 8").Grid!.Items.First(each => each.Item.Quality == ItemQuality.Normal && !each.Item.IsCompact);
            item = tile.Item;
            Main.Session.SelectedItem = tile;
            hint = Main.Home.UndoHint;
            program.EditPage().Superior.MakeCommand.Execute(null);
            program.EditPage().Superior.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => Main.Home.UndoHint != hint && Main.Home.UndoHint.StartsWith("Undo ", StringComparison.Ordinal)), "the edit is no step");

        program.On(() => Main.Home.UndoCommand.Execute(null));
        Assert.True(program.Until(() => Main.Session.SelectedItem is { } shown && !ReferenceEquals(shown.Item, item) && stash.Holds(shown.Item)),
            "the page stayed on the old item");
        program.Settled();
    }

    [CorpusFact]
    public void An_edit_to_an_item_just_stored_is_a_step_and_the_undo_takes_back_the_edit_not_the_storing()
    {
        if (!program.Ready(changes: true))
            return;


        using var free = program.InMode("Free");

        // Stored from the character, as its menu's Store in vault stores it with the vault beside: a save that writes the vault.
        int stored = 0;
        string hint = "";
        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Main.RightVault!.Show();
            program.GivePlain("crs", 2);
            stored = InVault;
            hint = Main.Home.UndoHint;
            Main.Inventory!.Menu(program.Tile("Crystal Sword")).Pick("Store in vault");
        });
        Assert.True(program.Until(() => InVault == stored + 1), "the sword was not stored");
        Assert.True(program.Until(() => Main.Home.UndoHint != hint && Main.Home.UndoHint.StartsWith("Undo ", StringComparison.Ordinal)),
            "the storing is no step: " + program.On(() => Main.Home.UndoHint));
        string storing = program.On(() => Main.Home.UndoHint);

        program.ShowOnTheVaultsEditPage("Crystal Sword");
        program.On(() =>
        {
            Superior.MakeCommand.Execute(null);
            Superior.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => program.Stored().Item.Quality == ItemQuality.Superior), "the vault's row is not superior");

        // The edit is the step the undo names, a moment after the vault's last write.
        Assert.True(program.Until(() => Main.Home.UndoHint.StartsWith("Undo Edited Crystal Sword.", StringComparison.Ordinal)),
            "the edit is no step: " + program.On(() => Main.Home.UndoHint));
        program.On(() => Main.Home.UndoCommand.Execute(null));

        // The edit is taken back, and nothing before it: the sword is in the vault still, a normal one, and the
        // storing is what the undo names next.
        Assert.True(program.Until(() => Main.Vault!.Cards().Count > 0 && program.Get<VaultService>().Chosen is not null
                                        && program.Stored().Item.Quality == ItemQuality.Normal), "the undo did not make it normal again");
        program.On(() =>
        {
            Assert.Equal(stored + 1, InVault);
            Assert.Equal(storing, Main.Home.UndoHint);
        });
    }
}
