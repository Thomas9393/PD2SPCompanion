using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// A section folded, every fold kept as a user setting: the Equipped
/// header's press folds the section and unfolds it, and a fold the settings hold is written to their file and
/// read back by the next start.
/// <para>
/// Not proven here: that a fold is kept the moment it is made and met again by the section (FoldMemory.Keep
/// and IsOpen). FoldMemory reads PD2SPC_FOLDS once, as it is first used, and this program - as every UI case -
/// runs with it off, so that a test meets every section as a new install has it; the last test here holds it
/// to that. The UI case folds-kept.folded-section-stays-folded-at-the-next-start runs with the folds kept and
/// shows the whole of it.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class FoldsTests(Program program)
{
    private const string Equipped = nameof(VP_ViewModel_Inv_L.EquipmentIsOpen);
    private const string OldVaultFold = "VaultFold.S13/Softcore/S13 Main.Left.42";

    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private EditorSettings Settings => program.Get<EditorSettings>();

    [CorpusFact]
    public void The_Equipped_headers_press_folds_the_section_and_a_second_press_unfolds_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            bool was = Page.EquipmentIsOpen;
            try
            {
                Page.EquipmentIsOpen = true;

                Page.ToggleEquipmentCommand.Execute(null);
                Assert.False(Page.EquipmentIsOpen);
                Assert.False(Page.ShowsEquippedPicks);

                Page.ToggleEquipmentCommand.Execute(null);
                Assert.True(Page.EquipmentIsOpen);
                Assert.True(Page.ShowsEquippedPicks);
            }
            finally
            {
                Page.EquipmentIsOpen = was;
            }
        });
    }

    [CorpusFact]
    public void A_fold_the_settings_hold_is_written_to_their_file_and_read_back_by_the_next_start()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                Settings.Folds[Equipped] = false;
                Settings.Folds["MercIsOpen"] = true;
                // A vault folder's fold as an older settings file keeps it, by the folder's id alone: let go on reading.
                // And as kept by the vault and the page: let go too, the tree's folds being a
                // session's (deliberately not kept across sessions).
                Settings.Folds["FolderPlace.12"] = false;
                Settings.Folds[OldVaultFold] = true;
                Settings.Save();

                string written = File.ReadAllText(EditorSettings.FilePath);
                Assert.Matches(@"""EquipmentIsOpen"":\s*false", written);
                Assert.Matches(@"""MercIsOpen"":\s*true", written);

                // What a start does first: the settings read from their file.
                var next = EditorSettings.Load();
                Assert.False(next.Folds[Equipped]);
                Assert.True(next.Folds["MercIsOpen"]);
                Assert.DoesNotContain("FolderPlace.12", next.Folds.Keys);
                Assert.DoesNotContain(OldVaultFold, next.Folds.Keys);
            }
            finally
            {
                Settings.Folds.Remove(Equipped);
                Settings.Folds.Remove("MercIsOpen");
                Settings.Folds.Remove("FolderPlace.12");
                Settings.Folds.Remove(OldVaultFold);
                Settings.Save();

                // Reading the settings hands them to the folds and to the Core: the program's own, again.
                FoldMemory.Settings = Settings;
                Settings.Apply();
            }
        });
    }

    [CorpusFact]
    public void With_the_folds_off_as_every_test_runs_a_section_folded_is_not_kept_and_none_kept_is_read()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            bool was = Page.EquipmentIsOpen;
            try
            {
                Page.EquipmentIsOpen = true;
                Page.ToggleEquipmentCommand.Execute(null);
                Assert.DoesNotContain(Equipped, Settings.Folds.Keys);
                Assert.DoesNotMatch("\"EquipmentIsOpen\"", File.Exists(EditorSettings.FilePath) ? File.ReadAllText(EditorSettings.FilePath) : "");

                // One the settings do hold is passed over: every section as it is made.
                Settings.Folds[Equipped] = false;
                Assert.True(FoldMemory.IsOpen(Equipped));
                Assert.False(FoldMemory.IsOpen(Equipped, initially: false));
            }
            finally
            {
                Settings.Folds.Remove(Equipped);
                Page.EquipmentIsOpen = was;
            }
        });
    }
}
