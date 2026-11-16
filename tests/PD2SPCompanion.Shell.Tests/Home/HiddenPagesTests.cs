using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// Pages kept hidden do their counting when shown (WhenShown): Settings' backup line and the rune
/// wizard no longer recount on every move behind the Items page, but read afresh on each visit. Edits are
/// saved between visits, each leaving a backup; on every visit to Settings the backups shown are the backups
/// on disk, and the rune wizard shows its count.
/// <para>
/// The pages told of being shown and put away as the side bar and their views tell them. That the views do
/// tell them is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class HiddenPagesTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Settings_L Settings => program.Get<VP_ViewModel_Settings_L>();
    private VP_ViewModel_Extra_R Extra => program.Get<VP_ViewModel_Extra_R>();

    /// <summary>The backups this program keeps, as the Settings line counts them.</summary>
    private int OnDisk => Directory.Exists(Backups) ? Directory.GetFiles(Backups, "*.bak", SearchOption.AllDirectories).Length : 0;

    private string Backups => Path.Combine(program.Folder, "saves", "PD2SPCompanion Backups");

    private EquipSlotViewModel Gloves => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Gloves && !slot.IsSwap);

    /// <summary>The worn gloves' name, while they are off.</summary>
    private string _gloves = "";

    /// <summary>Settings shown by its side bar button, and its line read once the disk has been counted.</summary>
    private int VisitSettings()
    {
        program.On(() => Assert.True(Main.ShowPageByKey("Settings_L")));
        Assert.True(program.Until(() => Settings.BackupCount == OnDisk), $"Settings shows {program.On(() => Settings.BackupCount)} backups, {OnDisk} are on disk");
        return program.On(() =>
        {
            Assert.Matches(@"^\d+ files · ", Settings.BackupSummary);
            return Settings.BackupCount;
        });
    }

    [CorpusFact]
    public void On_every_visit_Settings_shows_the_backups_on_disk_not_recounted_behind_another_page()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            int first = VisitSettings();
            Assert.True(first > 0);

            // Away to the Items page: edits, each saved, each leaving a backup - until the disk holds another
            // number than the line showed (a save also thins the older backups, so the number may go either way).
            program.On(() =>
            {
                Settings.Hidden();
                Main.ShowPageByKey("Inventory_L");
            });
            for (int round = 0; round < 4 && (round < 2 || OnDisk == first); round++)
            {
                program.On(() =>
                {
                    if (Gloves.IsFilled)
                    {
                        _gloves = Gloves.Item!.Name;
                        Main.Inventory!.TakeOff(Gloves);
                    }
                    else
                    {
                        var tile = Main.Inventory!.Sections.First(section => section.Pages is not null).Pages!
                            .First(page => page.Label == "Inventory").Grid!.Items.First(each => each.Name == _gloves);
                        Main.Inventory.Equip(tile);
                    }

                    Assert.True(program.Get<AutoSave>().Flush());
                });
            }

            Assert.NotEqual(first, OnDisk);

            // Behind the Items page the line is not counted again, however long it is left.
            Assert.False(program.Until(() => Settings.BackupCount != first, 300), "counted while hidden");

            int after = VisitSettings();
            Assert.NotEqual(first, after);
        }
        finally
        {
            program.On(Settings.Hidden);
        }
    }

    [CorpusFact]
    public void On_every_visit_the_rune_wizard_shows_its_count()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Assert.True(Main.ShowPageByKey("Extra_L"));
                Assert.Matches(@"^\d+ of [1-9]\d* can be made$", Extra.Wizard.Summary);

                Extra.Hidden();
                Main.ShowPageByKey("Inventory_L");
                Assert.True(Main.ShowPageByKey("Extra_L"));
                Assert.Matches(@"^\d+ of [1-9]\d* can be made$", Extra.Wizard.Summary);
            });
        }
        finally
        {
            program.On(Extra.Hidden);
        }
    }
}
