using System.ComponentModel;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// An item open on the Edit page follows a play mode changed in Settings (FollowPlayMode): the page
/// is kept, not rebuilt, so its rows and the add control have to be
/// told. And what the mode says of a charged skill's maximum and of durability, which the rows and Basic hold.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PlayModeFollowsTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private EditorSettings Settings => program.Get<EditorSettings>();

    [CorpusFact]
    public void A_mode_changed_with_an_item_open_tells_its_rows_and_the_add_control_each_way()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Beast Necklace");
            var row = Edit.Stats.First(each => each.Roll is not null);
            var heard = new List<string?>();
            var page = new List<string?>();
            void Row(object? _, PropertyChangedEventArgs changed) => heard.Add(changed.PropertyName);
            void Page(object? _, PropertyChangedEventArgs changed) => page.Add(changed.PropertyName);
            row.PropertyChanged += Row;
            Edit.PropertyChanged += Page;
            try
            {
                Assert.True(row.CanDelete);
                Assert.True(Edit.CanAddStat);

                Settings.PlayMode = "Casual";
                Assert.Contains(nameof(StatRowViewModel.Maximum), heard);
                Assert.Contains(nameof(StatRowViewModel.Minimum), heard);
                Assert.Contains(nameof(StatRowViewModel.CanDelete), heard);
                Assert.Contains(nameof(VP_ViewModel_Inv_R.CanAddStat), page);
                Assert.False(row.CanDelete);
                Assert.False(Edit.CanAddStat);

                // The bin is gone, and the command it stood for does nothing either.
                row.DeleteCommand.Execute(null);
                Assert.False(row.IsDeleted);

                heard.Clear();
                page.Clear();
                Settings.PlayMode = "Free";
                Assert.Contains(nameof(StatRowViewModel.Maximum), heard);
                Assert.Contains(nameof(StatRowViewModel.CanDelete), heard);
                Assert.Contains(nameof(VP_ViewModel_Inv_R.CanAddStat), page);
                Assert.True(row.CanDelete);
            }
            finally
            {
                row.PropertyChanged -= Row;
                Edit.PropertyChanged -= Page;
                program.Deselect();
            }
        });
    }

    [CorpusFact]
    public void Every_panel_is_told_not_only_the_first_to_hear()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 1, "Crystal Sword");
        program.On(() =>
        {
            var vault = program.VaultEditPage();
            var page = new List<string?>();
            void Page(object? _, PropertyChangedEventArgs changed) => page.Add(changed.PropertyName);
            vault.PropertyChanged += Page;
            try
            {
                Settings.PlayMode = "Casual";
                Assert.Contains(nameof(VP_ViewModel_Inv_R.CanAddStat), page);
            }
            finally
            {
                vault.PropertyChanged -= Page;
            }
        });
    }

    /// <summary>A number set in Free and kept, then the mode gone to Casual: held there, not corrected by being read.</summary>
    [CorpusFact]
    public void A_number_set_past_its_range_in_Free_is_not_bad_in_Casual_with_the_row_still_open()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        StatRowViewModel row = null!;
        int past = 0;
        program.On(() =>
        {
            program.Select("Beast Necklace");
            row = Edit.Stats.First(each => each is { Roll: not null, CanEdit: true, IsCompact: true });
            past = row.Roll!.Max + 3;
            row.Value = past.ToString();
        });
        program.Settled();
        program.On(() =>
        {
            Assert.Equal(past.ToString(), row.Value);

            Settings.PlayMode = "Casual";
            Assert.Equal(past, row.Maximum);
            Assert.False(row.IsChanged);
            Assert.False(Edit.HasChanges);
        });
    }

    private static StatRowViewModel Charged(int holds, int roll) => new(
        new ItemStat { Stat = "item_charged_skill", Value = Math.Min(holds, 20), MaxCharges = holds, SkillId = 64, SkillLevel = 5 },
        new RollRange(roll, roll));

    [CorpusFact]
    public void Max_charges_follow_the_Details_setting_within_limits_in_Casual_and_any_value_in_Free()
    {
        if (!program.Ready())
            return;

        using var casual = program.InMode("Casual");
        program.On(() =>
        {
            // Within limits: the source's.
            var row = Charged(20, 20);
            Assert.Equal((20, 20), (row.MaxChargesMinimum, row.MaxChargesMaximum));
            row.JumpMaxCharges(up: true);
            Assert.Equal("20", row.MaxChargesText);
            row.MaxChargesText = "255";
            Assert.Equal("20", row.MaxChargesText);
            Assert.False(row.CanRaiseMaxCharges);
            Assert.False(row.IsChanged);

            // What the item came with stands, and can come down to the source.
            var above = Charged(50, 20);
            Assert.Equal((20, 50), (above.MaxChargesMinimum, above.MaxChargesMaximum));
            Assert.Equal("50", above.MaxChargesText);
            above.JumpMaxCharges(up: false);
            Assert.Equal("20", above.MaxChargesText);

            Settings.PlayMode = "Free";
            var any = Charged(20, 20);
            Assert.Equal((0, 255), (any.MaxChargesMinimum, any.MaxChargesMaximum));
            any.JumpMaxCharges(up: true);
            Assert.Equal("255", any.MaxChargesText);
        });
    }

    [CorpusFact]
    public void Max_charges_locked_by_Details_stay_where_they_are()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var was = StatRowViewModel.ChargesReach;
            StatRowViewModel.ChargesReach = Reach.Locked;
            try
            {
                var row = Charged(20, 20);
                Assert.True(row.MaxChargesLocked);
                Assert.Equal((20, 20), (row.MaxChargesMinimum, row.MaxChargesMaximum));
                row.MaxChargesText = "5";
                Assert.Equal("20", row.MaxChargesText);
            }
            finally
            {
                StatRowViewModel.ChargesReach = was;
            }
        });
    }

    /// <summary>The maximum typed under the current one takes it down in the file, as Commit's own words say and the summary showed.</summary>
    [CorpusFact]
    public void Max_durability_lowered_under_the_current_takes_the_current_down_with_it()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item armor = null!;
        program.On(() =>
        {
            armor = program.GivePlain("cap", 0);
            armor.MaxDurability = 100;
            armor.Durability = 100;
            program.Main.Session.SelectedItem = program.TileOf(armor);
            Assert.Equal("100", Edit.Basic.Durability);

            Edit.Basic.MaxDurability = "50";
        });

        Assert.True(program.Until(() => armor.MaxDurability == 50), "the maximum was not written");
        program.Settled();
        program.On(() =>
        {
            Assert.Equal(50, armor.Durability);
            Assert.Equal("50", Edit.Basic.Durability);
            Assert.False(Edit.HasChanges);
        });
    }

    [CorpusFact]
    public void Max_durability_lowered_over_the_current_leaves_the_current()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item armor = null!;
        program.On(() =>
        {
            armor = program.GivePlain("cap", 0);
            armor.MaxDurability = 100;
            armor.Durability = 30;
            program.Main.Session.SelectedItem = program.TileOf(armor);
            Edit.Basic.MaxDurability = "50";
        });

        Assert.True(program.Until(() => armor.MaxDurability == 50), "the maximum was not written");
        program.Settled();
        program.On(() => Assert.Equal(30, armor.Durability));
    }
}
