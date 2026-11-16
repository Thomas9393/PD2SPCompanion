using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A superior item's bonus on the Edit page,
/// on a white Crystal Sword in the vault: the item's holder offers Make
/// superior; pressed, the item is superior - its summary says so - and the picker opens on the rows a weapon
/// can carry; closed with nothing picked it is a superior item with no bonus, and the undo takes that back. A
/// row picked is the item's, its line names the row, another row takes its place, and None takes the bonus
/// off.
/// <para>
/// What the editor and the page decide, asked through the editor's commands and read back from the item's
/// summary and the vault's own row. Nothing of it is the window's alone.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SuperiorTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.VaultEditPage();
    private SuperiorEditor Superior => Edit.Superior;

    private SuperiorChoiceRow Choice(string line) => Superior.Choices.First(row => row.Lines.Contains(line));

    private bool Reads(string start) => Edit.Lines().Any(line => line.StartsWith(start, StringComparison.Ordinal));

    [CorpusFact]
    public void A_normal_item_made_superior_opens_the_picker_on_a_weapons_rows_and_the_undo_takes_it_back()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 2, "Crystal Sword");

        program.On(() =>
        {
            Assert.Contains("Rarity: Normal", Edit.Summary!.SummaryFacts);
            Assert.True(Superior.CanMake);
            Assert.False(Superior.Shows);

            Superior.MakeCommand.Execute(null);
            Assert.True(Superior.IsPickerOpen);
            Assert.Equal("Crystal Sword: superior.", program.Main.Session.RightMessages.Text);

            var offered = Superior.Choices.SelectMany(row => row.Lines).ToList();
            Assert.DoesNotContain("None", offered);
            Assert.Contains("+10-20% Enhanced Damage", offered);
            Assert.Contains("15-25% Bonus to Attack Rating", offered);
            Assert.Contains("Increase Maximum Durability 15-25%", offered);
            Assert.DoesNotContain(offered, line => line.Contains("Enhanced Defense"));

            // Closed with nothing picked: kept so.
            Superior.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => program.Stored().Item.Quality == ItemQuality.Superior), "the vault's row is not superior");
        program.On(() =>
        {
            Assert.Contains("Rarity: Superior", Edit.Summary!.SummaryFacts);
            Assert.True(Superior.Shows);
            Assert.Equal("None", Superior.Now);
            Assert.False(Superior.CanMake);

        });

        // The undo, once the change is a step of the history: the vault's steps are taken a moment after its last write.
        Assert.True(program.Until(() => program.Main.Home.UndoHint.StartsWith("Undo Edited Crystal Sword.", StringComparison.Ordinal)),
            program.On(() => program.Main.Home.UndoHint));
        program.On(() => program.Main.Home.UndoCommand.Execute(null));

        // The vault is read again by the undo: the sword is the page's item still, a normal one.
        Assert.True(program.Until(() => program.Main.Vault!.Cards().Count > 0 && Superior.CanMake && program.Get<VaultService>().Chosen is not null
                                        && program.Stored().Item.Quality == ItemQuality.Normal), "the undo did not make it normal again");
        program.On(() =>
        {
            Assert.Contains("Rarity: Normal", Edit.Summary!.SummaryFacts);
            Assert.False(Superior.Shows);
        });
    }

    [CorpusFact]
    public void A_row_picked_is_the_items_bonus_another_takes_its_place_and_None_takes_it_off()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 2, "Crystal Sword");

        program.On(() =>
        {
            Superior.MakeCommand.Execute(null);
            Superior.PickCommand.Execute(Choice("+10-20% Enhanced Damage"));

            // A pick leaves the picker open, as the corruption's does, and is the item's once it
            // closes: the top of the row's range, and the damage with it.
            Assert.True(Superior.IsPickerOpen);
            Superior.ClosePickerCommand.Execute(null);
        });
        program.Settled(Edit);
        Assert.True(program.Until(() => program.Stored().Item.StatOf("item_maxdamage_percent") == 20), "the vault's row has no +20% Enhanced Damage");

        program.On(() =>
        {
            Assert.Contains("Rarity: Superior", Edit.Summary!.SummaryFacts);
            Assert.True(Reads("+20% Enhanced Damage"));
            Assert.Contains("One-Hand Damage: 6 to 18", Edit.Lines());
            Assert.Equal("+10-20% Enhanced Damage", Superior.Now);
            Assert.True(Choice("+10-20% Enhanced Damage").IsHeld);

            Superior.OpenPickerCommand.Execute(null);
            Superior.PickCommand.Execute(Choice("15-25% Bonus to Attack Rating"));
            // Written when the picker closes: it stays open after a pick.
            Superior.ClosePickerCommand.Execute(null);
        });
        program.Settled(Edit);

        program.On(() =>
        {
            Assert.True(Reads("25% Bonus to Attack Rating"));
            Assert.False(Reads("+20% Enhanced Damage"));
            Assert.Equal("15-25% Bonus to Attack Rating", Superior.Now);

            Superior.OpenPickerCommand.Execute(null);
            Assert.DoesNotContain(Superior.Choices, row => row.Bonus is null);
            Superior.TakeOffCommand.Execute(null);
            // Written when the picker closes: it stays open after a pick.
            Superior.ClosePickerCommand.Execute(null);
        });
        program.Settled(Edit);

        program.On(() =>
        {
            Assert.False(Reads("25% Bonus to Attack Rating"));
            Assert.Equal("None", Superior.Now);
            Assert.True(Superior.HasNone);
            Assert.Equal("Crystal Sword: superior bonus removed.", program.Main.Session.RightMessages.Text);

            // Superior still: None is a superior item with no bonus.
            Assert.Contains("Rarity: Superior", Edit.Summary!.SummaryFacts);
        });
    }
}
