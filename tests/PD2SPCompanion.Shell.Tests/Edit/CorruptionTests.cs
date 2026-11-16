using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// Corruption and desecration through their own pickers on the Edit page (CorruptionEditor, DesecrationEditor):
/// a roll is staged at once and the picker stays open on it, a
/// second roll taking the first one's place, and what stands when the picker closes is the item's - one
/// corruption, however many rolls. A clean takes it off again. The lists after the item's own - a set's
/// bonuses - are left as they were: a corruption set here must not remove a set bonus. A
/// corrupted amulet is desecrated the same way and stays corrupted. And of none of it has the legitimacy check
/// anything to say.
/// <para>
/// What the pickers and the page decide, asked through their commands and read back from the item and the
/// item's summary. That the pickers' buttons are there to press is one UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CorruptionTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();
    private CorruptionEditor Corruption => Edit.Corruption;
    private DesecrationEditor Desecration => Edit.Desecration;

    private string Said => program.Main.Session.RightMessages.Text;

    /// <summary>
    /// The one <c>corruptor</c> a corruption leaves, as Season 14's cube does: 1001 and the roll inside the outcome's band
    /// (Corruptions.FlagFor; PhysSpear's Atma's Scarab read 1470 in game). Season 13's one bit held 1.
    /// </summary>
    private static void Rolled(Item item) => Assert.InRange(Assert.Single(item.Every(Corruptions.FlagStat)), 1002, 2001);

    /// <summary>The cube's own roll in the picker, the picker closed, and the change waited for.</summary>
    private Item Corrupt(string name, string? container = null, int rolls = 1)
    {
        Item item = null!;
        program.On(() =>
        {
            item = program.Select(name, container);
            Assert.True(Corruption.CanCorrupt);

            Corruption.OpenPickerCommand.Execute(null);
            for (int roll = 0; roll < rolls; roll++)
            {
                Corruption.RollCommand.Execute(null);

                // The picker stays open on what was rolled, and while that is only staged another roll takes its place.
                Assert.True(Corruption.IsPickerOpen);
                Assert.True(Corruption.HoldsMark);
                Assert.True(Corruption.CanPick);
                Assert.StartsWith($"{name}: Corrupted: ", Said);
                Assert.Empty(item.Every(Corruptions.FlagStat));
            }

            Corruption.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => item.Every(Corruptions.FlagStat).Count > 0), "the corruption was not written");
        program.Settled();
        return item;
    }

    /// <summary>
    /// A socket affix picked on an item a corruption gave sockets takes the corruption off and gives the affix's own
    /// (the item stayed at the corruption's count until the corruption was removed).
    /// </summary>
    [CorpusFact]
    public void A_socket_affix_picked_on_a_socket_corruption_cleans_it()
    {
        if (!program.Ready(changes: true))
            return;

        Item item = null!;
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var tile = program.Tiles().First(each => each.Item.Quality == ItemQuality.Rare
                && each.Item.TotalNumberOfSockets == 0 && Corruptions.Available(each.Item).Any(option => option.GivesSockets));
            item = tile.Item;
            Corruptions.Apply(item, Corruptions.Available(item).Last(option => option.GivesSockets), new Random(3));
            program.Main.Session.SelectedItem = tile;
            Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(item));

            var affixes = Edit.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            affixes.AimAffixCommand.Execute("0:prefix");
            affixes.AffixSearch = "";
            var row = affixes.AffixChoices.First(each => each.CanPick
                && each.Option.Stats.Contains(ItemForge.SocketCount, StringComparer.OrdinalIgnoreCase));
            affixes.ChooseAffixCommand.Execute(row);
            affixes.CloseAffixPickerCommand.Execute(null);
            Assert.EndsWith("affixes changed, socket corruption cleaned.", Said);
        });

        Assert.True(program.Until(() => Corruptions.IdOn(item) != Corruptions.Sockets), "the corruption stayed");
        program.Settled();
        program.On(() =>
        {
            Assert.False(Corruptions.IsCorrupted(item));
            Assert.Empty(item.Every(Corruptions.FlagStat));
            Assert.True(item.TotalNumberOfSockets > 0);
        });
    }

    /// <summary>
    /// On the vault's Edit page gems do not come out, so a clean that closes filled sockets can never be written. It said
    /// "cleaned" and showed the item clean, and the next selection dropped the change without a word.
    /// </summary>
    [CorpusFact]
    public void A_clean_that_would_close_filled_sockets_on_the_vault_page_is_dimmed_and_refused_and_says_why()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        string name = program.On(() =>
        {
            var vaults = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
            var era = program.Main.Session.ItemEra!;
            var made = ItemForge.PlainBase("crs", 0, era).Item;
            made.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            Corruptions.Apply(made, Corruptions.Available(made).First(each => each.GivesSockets), new Random(3));
            Assert.True(made.TotalNumberOfSockets > 0);
            ItemForge.Socket(made, ItemForge.Material("r01", 1, era));

            Assert.NotNull(vaults.Store!.Add(made, vaults.CurrentFolder, out _));
            vaults.SayChanged();
            return ItemPresenter.Describe(made).Name;
        });
        program.ShowOnTheVaultsEditPage(name);

        program.On(() =>
        {
            var page = program.VaultEditPage();
            var corruption = page.Corruption;
            var item = page.Session.SelectedItem!.Item;
            Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(item));
            Assert.Equal(1, item.Filled());

            Assert.True(corruption.HoldsMark);
            Assert.False(corruption.CanClean);
            Assert.Contains("do not come out here", corruption.CleanHint);

            int sockets = item.TotalNumberOfSockets;
            corruption.CleanCommand.Execute(null);

            Assert.Contains("do not come out here", Said);
            Assert.DoesNotContain("cleaned", Said);
            Assert.False(page.HasChanges);
            Assert.Equal(Corruptions.Sockets, Corruptions.IdOn(item));
            Assert.Equal(sockets, item.TotalNumberOfSockets);
        });
    }

    [CorpusFact]
    public void Three_rolls_in_the_picker_leave_one_corruption_on_the_item_and_the_summary_says_so()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        var amulet = Corrupt("Beast Necklace", rolls: 3);

        program.On(() =>
        {
            Rolled(amulet);
            Assert.Single(amulet.Every(Corruptions.IdStat));
            Assert.True(Corruption.IsCorrupted);
            Assert.False(Corruption.CanCorrupt);
            Assert.False(Corruption.IsPickerOpen);
            Assert.Contains("Corrupted", Edit.Lines());

            // The two a corruption keeps for itself are not properties to edit.
            Assert.DoesNotContain(Edit.Stats, row => row.Name is Corruptions.IdStat or Corruptions.FlagStat);
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void A_clean_in_the_picker_takes_the_corruption_off_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        var amulet = Corrupt("Beast Necklace");

        program.On(() =>
        {
            Assert.StartsWith("Clean corruption", Corruption.CleanHint);
            Corruption.OpenPickerCommand.Execute(null);
            Assert.True(Corruption.CanPick);

            Corruption.CleanCommand.Execute(null);
            Assert.StartsWith("Beast Necklace: Corruption cleaned", Said);

            // The picker stays open on the clean item, for a new pick.
            Assert.True(Corruption.IsPickerOpen);
            Assert.True(Corruption.CanPick);
            Corruption.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => amulet.Every(Corruptions.FlagStat).Count == 0), "the corruption was not taken off");
        program.Settled();
        program.On(() =>
        {
            Assert.Empty(amulet.Every(Corruptions.IdStat));
            Assert.False(Corruption.IsCorrupted);
            Assert.True(Corruption.CanCorrupt);
            Assert.DoesNotContain("Corrupted", Edit.Lines());
            Assert.Empty(Edit.Findings());
        });
    }

    /// <summary>
    /// The corruption picker should not need a clean and a new pick. A pick on an item whose
    /// corruption is written takes its place in one step - the clean and the new one together - and so does a
    /// roll; one corruption on the item after.
    /// </summary>
    [CorpusFact]
    public void A_pick_on_a_corrupted_item_takes_its_corruptions_place_in_one_step()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        var amulet = Corrupt("Beast Necklace");
        int was = amulet.Every(Corruptions.IdStat).Single();
        int picked = 0;

        program.On(() =>
        {
            Corruption.OpenPickerCommand.Execute(null);
            Assert.True(Corruption.CanPick);

            var row = Corruption.Choices.SelectMany(tier => tier.Options).First(option => !option.IsHeld && option.Option.Id != was);
            picked = row.Option.Id;
            Corruption.PickCommand.Execute(row);
            Assert.StartsWith("Beast Necklace: Corruption cleaned", Said);
            Assert.Contains("Corrupted: ", Said);
            Assert.True(Corruption.IsPickerOpen);
            Assert.Contains(Corruption.Choices.SelectMany(tier => tier.Options), option => option.IsHeld && option.Option.Id == picked);

            Corruption.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => amulet.Every(Corruptions.IdStat) is [var id] && id == picked), "the pick did not take the corruption's place");
        program.Settled();

        // And the skull, on the item as written now.
        program.On(() =>
        {
            Corruption.OpenPickerCommand.Execute(null);
            Corruption.RollCommand.Execute(null);
            Assert.StartsWith("Beast Necklace: Corruption cleaned", Said);
            Assert.Contains("Corrupted: ", Said);
            Corruption.ClosePickerCommand.Execute(null);
        });
        program.Settled();
        program.On(() =>
        {
            Rolled(amulet);
            Assert.Single(amulet.Every(Corruptions.IdStat));
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void A_set_piece_corrupted_keeps_its_set_bonus_lists_as_they_were()
    {
        if (!program.Ready(changes: true))
            return;

        // The lists after the first, as the file has them: a set's bonuses.
        static string LaterLists(Item item) => string.Join(" | ", item.StatLists.Skip(1).Select(list =>
            string.Join(", ", list.Stats.Select(stat => $"{stat.Stat} {stat.Param} {stat.Value}"))));

        using var free = program.InMode("Free");
        string was = "";
        program.On(() =>
        {
            program.Panes("Personal Stash");
            var piece = program.Select("Tancred's Weird", "Personal Stash");
            was = LaterLists(piece);
            Assert.True(was.Length > 0 && piece.StatLists.Count > 1, "Tancred's Weird has its set lists");
            Assert.Contains(Edit.Stats, row => row.IsSetBonus);
        });

        var set = Corrupt("Tancred's Weird", "Personal Stash");

        program.On(() =>
        {
            Rolled(set);
            Assert.Equal(was, LaterLists(set));
            Assert.Contains(Edit.Stats, row => row.IsSetBonus);
            Assert.Empty(Edit.Findings());
        });
    }

    [CorpusFact]
    public void A_corrupted_amulet_is_desecrated_in_its_picker_and_stays_corrupted()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item amulet = null!;
        program.On(() =>
        {
            // Corrupted first, with the corruption the old script gave it (pd2spc corrupt --id 68).
            amulet = program.Select("Beast Necklace");
            Assert.False(Desecration.CanDesecrate);

            Corruption.OpenPickerCommand.Execute(null);
            Corruption.PickCommand.Execute(Corruption.Choices.SelectMany(tier => tier.Options).First(row => row.Option.Id == 68));
            Corruption.ClosePickerCommand.Execute(null);
        });
        Assert.True(program.Until(() => amulet.Every(Corruptions.IdStat).SequenceEqual([68])), "the amulet was not corrupted");
        program.Settled();

        program.On(() =>
        {
            Assert.True(Desecration.CanDesecrate);
            Assert.False(Desecration.IsDesecrated);
            Assert.Empty(amulet.Every(Desecrations.FlagStat));

            Desecration.OpenPickerCommand.Execute(null);
            Desecration.RollCommand.Execute(null);

            // A roll is the change, and the picker stays open, as the corruption's does -
            // written when it is closed, as every picker's change is on a page that keeps at once.
            Assert.True(Desecration.IsPickerOpen);
            Desecration.ClosePickerCommand.Execute(null);
            Assert.StartsWith("Beast Necklace: Desecrated: ", Said);
        });

        Assert.True(program.Until(() => amulet.Every(Desecrations.FlagStat).Count > 0), "the desecration was not written");
        program.Settled();
        program.On(() =>
        {
            Assert.Equal([1001], amulet.Every(Desecrations.FlagStat));   // Season 14's (Desecrations.FlagFor)
            Assert.Single(amulet.Every(Desecrations.ValueStat));
            Rolled(amulet);
            Assert.True(Desecration.IsDesecrated);
            Assert.Contains("Desecrated", Edit.Lines());
            Assert.Contains("Corrupted", Edit.Lines());
            Assert.Empty(Edit.Findings());

            // The mod it was given is never asked about, and nothing waits on an answer.
            Assert.False(Desecration.IsUnclear);
            Assert.False(Desecration.AwaitsAnswer);
            Assert.True(Desecration.CanClean);

            // Another pick takes its place, in one step: no clean first.
            Desecration.OpenPickerCommand.Execute(null);
            Assert.True(Desecration.CanPick);
            var other = Desecration.Choices.First(row => !row.IsHeld);
            Desecration.PickCommand.Execute(other);
            Assert.True(Desecration.IsPickerOpen);
            Assert.Equal(other.Option.Id, Desecration.Choices.Single(row => row.IsHeld).Option.Id);
            Assert.False(Desecration.IsUnclear);
            Desecration.ClosePickerCommand.Execute(null);
        });

        Assert.True(program.Until(() => amulet.Every(Desecrations.FlagStat).Count == 1 && Edit.Findings().Count == 0),
            program.On(() => string.Join(" | ", Edit.Findings())));
    }
}
