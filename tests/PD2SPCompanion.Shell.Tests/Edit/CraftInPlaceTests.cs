using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A craft is made in place: the item keeps its place and its id, turns crafted, and no
/// copy appears - a magic ring in the personal stash, the worn rare amulet, and worn boots stored in the vault
/// and crafted on the vault's Edit page. A crafted item's recipe is changed from its Crafting line the same
/// way, and the legitimacy check has nothing to say of any of them. In Casual, where Store moves an item into
/// the vault.
/// <para>
/// What the crafting editor and the pages decide, asked through their commands and read back from the items
/// and the vault's own row. That the Craft button and a family's row are there to press is one UI case's to
/// show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CraftInPlaceTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private CraftingEditor Crafting => Edit.Crafting;

    private string Said => program.Main.Session.RightMessages.Text;

    /// <summary>Every item the character holds, wherever: worn, the mercenary's, in a grid of its own.</summary>
    private int Held =>
        program.Main.Session.Equipped.Count(slot => slot.IsFilled) + program.Main.Session.MercEquipped.Count(slot => slot.IsFilled)
        + program.CountIn("Inventory") + program.CountIn("Cube") + program.CountIn("Personal Stash");

    /// <summary>The Craft button's picker, and the first family it offers picked.</summary>
    private static string CraftFirst(CraftingEditor crafting)
    {
        Assert.True(crafting.CanCraftFrom);
        crafting.CraftFromItemCommand.Execute(null);
        Assert.True(crafting.IsPickerOpen);
        Assert.True(crafting.IsCraftingFrom);

        var family = crafting.Choices[0];
        crafting.PickCommand.Execute(family);
        // Open still, as every picker is after a pick; written when it closes.
        Assert.True(crafting.IsPickerOpen);
        crafting.ClosePickerCommand.Execute(null);
        return family.Family;
    }

    /// <summary>
    /// What comes out of a craft does not carry the corruption or the desecration (CraftingEditor.CanCraftFrom): the
    /// craft took their lines off, and the pair that marks each stayed, so the crafted item read as corrupted.
    /// </summary>
    [CorpusFact]
    public void A_craft_takes_the_mark_of_a_corruption_and_of_a_desecration_off_with_their_lines()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            Edit.Corruption.OpenPickerCommand.Execute(null);
            Edit.Corruption.PickCommand.Execute(Edit.Corruption.Choices.SelectMany(tier => tier.Options).First(row => row.Option.Id == 68));
            Edit.Corruption.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => amulet.Every(PD2SPCompanion.Core.Items.Corruptions.IdStat).SequenceEqual([68])), "the amulet was not corrupted");
        program.Settled();

        program.On(() =>
        {
            Edit.Desecration.OpenPickerCommand.Execute(null);
            Edit.Desecration.RollCommand.Execute(null);
            Edit.Desecration.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => amulet.Every(PD2SPCompanion.Core.Items.Desecrations.FlagStat).Count > 0), "the desecration was not written");
        program.Settled();

        program.On(() =>
        {
            Assert.True(Crafting.CanCraftFrom);
            CraftFirst(Crafting);
        });
        Assert.True(program.Until(() => amulet.Quality == ItemQuality.Craft), "the amulet was not crafted");
        program.Settled();

        program.On(() =>
        {
            Assert.Empty(amulet.Every(PD2SPCompanion.Core.Items.Corruptions.IdStat));
            Assert.Empty(amulet.Every(PD2SPCompanion.Core.Items.Corruptions.FlagStat));
            Assert.Empty(amulet.Every(PD2SPCompanion.Core.Items.Desecrations.FlagStat));
            Assert.Empty(amulet.Every(PD2SPCompanion.Core.Items.Desecrations.ValueStat));
            Assert.False(Edit.Corruption.IsCorrupted);
            Assert.False(Edit.Desecration.IsDesecrated);
            Assert.DoesNotContain("Corrupted", Edit.Lines());
            Assert.DoesNotContain("Desecrated", Edit.Lines());
        });
    }

    /// <summary>The cube's new item keeps no empty socket, as a socket corruption's helm read in game; the page's row goes with it.</summary>
    [CorpusFact]
    public void A_craft_closes_an_empty_socket_on_the_page_and_on_the_item()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            amulet.TotalNumberOfSockets = 1;
            amulet.IsSocketed = true;
            Edit.Sockets.StageFrom(amulet);
            CraftFirst(Crafting);
        });
        Assert.True(program.Until(() => amulet.Quality == ItemQuality.Craft), "the amulet was not crafted");
        program.Settled();

        program.On(() =>
        {
            Assert.Equal(0, amulet.TotalNumberOfSockets);
            Assert.False(amulet.IsSocketed);
            Assert.False(Edit.Sockets.HasSockets);
        });
    }

    [CorpusFact]
    public void A_ring_in_the_stash_is_crafted_where_it_lies_under_its_own_id_and_its_recipe_can_be_changed()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        Item ring = null!;
        uint id = 0;
        int held = 0;
        string family = "";
        program.On(() =>
        {
            program.Panes("Personal Stash");
            ring = program.Select("Steel Ring of Skill", "Personal Stash");
            Assert.Equal(ItemQuality.Magic, ring.Quality);
            Assert.Equal((1, 1), (ring.X, ring.Y));
            (id, held) = (ring.Id, Held);

            family = CraftFirst(Crafting);
            // Named as it was: the craft gives it another name, and the strip says what was changed.
            Assert.Equal($"Steel Ring of Skill: now {family.ToLowerInvariant()}.", Said);
        });

        Assert.True(program.Until(() => ring.Quality == ItemQuality.Craft), "the ring was not crafted");
        program.Settled();
        program.On(() =>
        {
            Assert.Equal(id, ring.Id);
            Assert.Equal((1, 1), (ring.X, ring.Y));
            Assert.Same(ring, program.Choice("Personal Stash").Grid!.Items.First(tile => tile.Item.X == 1 && tile.Item.Y == 1).Item);
            Assert.Equal(held, Held);
            Assert.True(Crafting.IsCraft);
            Assert.Equal(family, Crafting.Family);
            Assert.False(Crafting.CanCraftFrom);
            Assert.Empty(Edit.Findings());

            // Another family from the Crafting line's picker: the recipe changed, not a second craft.
            Crafting.OpenPickerCommand.Execute(null);
            Assert.False(Crafting.IsCraftingFrom);
            var other = Crafting.Choices.First(row => row.Family != family);
            family = other.Family;
            Crafting.PickCommand.Execute(other);
            // Written when the picker closes: it stays open after a pick.
            Crafting.ClosePickerCommand.Execute(null);
        });

        program.Settled();
        program.On(() =>
        {
            Assert.Equal(family, Crafting.Family);
            Assert.Equal(ItemQuality.Craft, ring.Quality);
            Assert.Equal(id, ring.Id);
            Assert.Equal(held, Held);
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void The_worn_amulet_is_crafted_where_it_is_worn_at_the_cubes_item_level()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        Item amulet = null!;
        uint id = 0;
        int held = 0;
        program.On(() =>
        {
            amulet = program.Select("Beast Necklace");
            Assert.Equal(ItemQuality.Rare, amulet.Quality);
            (id, held) = (amulet.Id, Held);
            CraftFirst(Crafting);
        });

        Assert.True(program.Until(() => amulet.Quality == ItemQuality.Craft), "the amulet was not crafted");
        program.Settled();
        program.On(() =>
        {
            Assert.Equal(id, amulet.Id);
            Assert.Equal(83, amulet.ItemLevel);
            Assert.Same(amulet, program.Worn(ItemLocation.Neck).Item!.Item);
            Assert.Equal(held, Held);
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void Boots_stored_in_the_vault_are_crafted_there_on_the_vaults_Edit_page()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        var main = program.Main;
        var store = program.On(() => program.Get<VaultService>().Store!);
        int held = 0, stored = 0;
        program.On(() =>
        {
            (held, stored) = (Held, store.Count());

            // Store, on the worn boots: in Casual it moves them into the vault.
            program.Select("Bramble Track");
            main.Inventory!.Store(VP_ViewModel_Inv_L.StoreAs.Open);
        });
        Assert.True(program.Until(() => store.Count() == stored + 1), "the boots were not stored");

        program.On(() =>
        {
            Assert.Equal(held - 1, Held);
            Assert.False(program.Worn(ItemLocation.Feet).IsFilled);
            main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            main.Vault!.Show();
        });
        Assert.True(program.Until(() => main.Vault!.Cards().Any(card => card.Name == "Bramble Track")), "the boots' card was not listed");
        program.On(() => main.Vault!.SelectOnly(main.Vault.Cards().First(card => card.Name == "Bramble Track")));
        Assert.True(program.Until(() => program.VaultEditPage().Session.SelectedItem?.Name == "Bramble Track"), "the boots are not on the vault's Edit page");

        program.On(() => CraftFirst(program.VaultEditPage().Crafting));

        Assert.True(program.Until(() => (ItemQuality)program.Stored().Row.Quality == ItemQuality.Craft), "the vault's row is not crafted");
        program.On(() =>
        {
            Assert.Equal(ItemQuality.Craft, program.Stored().Item.Quality);
            Assert.Equal(stored + 1, store.Count());
            Assert.Equal(held - 1, Held);
        });
    }
}
