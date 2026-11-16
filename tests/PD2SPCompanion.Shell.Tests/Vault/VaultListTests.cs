using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A mode's several vaults, all from Settings' Vault row:
/// its box says the open vault and opens the list;
/// there a vault is made, another copied, a row clicked opens that one and it is remembered. And the one
/// Import: a file comes in as a folder of the open vault, the folders ticked - one unticked stays out - or as
/// a vault of its own, which is opened.
/// <para>
/// What the Vault row decides, asked of its view model as the buttons ask and read back from the strip, the
/// settings and the files. The file picker is answered for the test (PD2SPC_PICK_OPEN), as it was for the
/// old script. That the box opens the list over the page, that a name typed and Enter make a vault, and that
/// the import's ticks are there to click, are the UI cases' to show (units/vault-list.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultListTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);
    private readonly string? _picked = Environment.GetEnvironmentVariable("PD2SPC_PICK_OPEN");

    private VaultManagerViewModel Vaults => program.Get<VP_ViewModel_Settings_R>().Vaults!;

    /// <summary>The mode's first vault, open as a test begins.</summary>
    private string First => _book.Vaults.VaultName;

    private string Status => _book.Session.Status;

    private VaultListRow Row(string name) => Vaults.Rows.First(row => row.Name == name);

    /// <summary>The file a vault of the open mode is kept in.</summary>
    private string FileOf(string name) =>
        Path.Combine(Path.GetDirectoryName(_book.Store.FilePath)!, name + VaultLibrary.StoredExtension);

    /// <summary>New vault, a name typed, Enter.</summary>
    private void Make(string name)
    {
        Vaults.OpenPickerCommand.Execute(null);
        Vaults.StartNewCommand.Execute(null);
        Assert.True(Vaults.IsNaming);
        Vaults.NewName = name;
        Vaults.CommitNewCommand.Execute(null);
    }

    /// <summary>
    /// The pad's own vault written out as Shared.pd2vault - 6 items: one at its top, three under Charms (none
    /// of its own), and Jewels with one of its own and one in Magic - and Import pressed, the picker answering
    /// with it.
    /// </summary>
    private void Import(string pads)
    {
        program.On(() =>
        {
            string file = Path.Combine(program.Folder, "Shared" + VaultLibrary.VaultExtension);
            _book.Vaults.ExportVault(pads, file);
            Assert.True(File.Exists(file));

            Environment.SetEnvironmentVariable("PD2SPC_PICK_OPEN", file);
            Vaults.OpenPickerCommand.Execute(null);
            Vaults.ImportCommand.Execute(null);
        });
        _book.Until(() => Vaults.IsImportOpen, "the import's panel did not open");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_PICK_OPEN", _picked);
        if (program.Available)
        {
            program.On(() =>
            {
                Vaults.CloseImportCommand.Execute(null);
                Vaults.ClosePickerCommand.Execute(null);
            });
            File.Delete(Path.Combine(program.Folder, "Shared" + VaultLibrary.VaultExtension));
        }

        _book.PutAway();
    }

    [CorpusFact]
    public void The_Vault_rows_box_says_the_open_vault_and_its_list_has_the_modes_vaults()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Vaults.OpenPickerCommand.Execute(null);

            Assert.True(Vaults.IsPickerOpen);
            Assert.Equal(First, Vaults.OpenName);
            Assert.True(Row(First).IsOpen);
            Assert.StartsWith("6 items", Row(First).Summary);
        });
    }

    /// <summary>Esc in the name's box, or the box's own way out: no vault is made of what was typed.</summary>
    [CorpusFact]
    public void A_new_vaults_name_let_go_of_makes_no_vault()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            int rows = Vaults.Rows.Count;
            Vaults.OpenPickerCommand.Execute(null);
            Vaults.StartNewCommand.Execute(null);
            Assert.True(Vaults.IsNaming);
            Vaults.NewName = "Never";

            Vaults.CancelNewCommand.Execute(null);

            Assert.False(Vaults.IsNaming);
            Assert.Equal(rows, Vaults.Rows.Count);
            Assert.DoesNotContain(Vaults.Rows, row => row.Name == "Never");
            Assert.False(File.Exists(FileOf("Never")));
            Assert.Equal(First, Vaults.OpenName);
        });
    }

    [CorpusFact]
    public void New_vault_makes_an_empty_one_of_the_modes_and_leaves_the_open_one_open()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Make("Playtest");

            Assert.Equal("Created Playtest.", Status);
            Assert.False(Vaults.IsNaming);
            Assert.Equal("0 items", Row("Playtest").Summary);
            Assert.False(Row("Playtest").IsOpen);
            Assert.True(File.Exists(FileOf("Playtest")));
            Assert.Equal(First, Vaults.OpenName);
        });
    }

    [CorpusFact]
    public void Copy_makes_a_vault_with_everything_of_the_first_in_it_its_name_box_open()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Vaults.OpenPickerCommand.Execute(null);
            Vaults.CopyVaultCommand.Execute(Row(First));

            string copy = $"{First} (2)";
            Assert.Equal($"Cloned {First} as {copy}.", Status);
            Assert.True(File.Exists(FileOf(copy)));
            Assert.StartsWith("6 items", Row(copy).Summary);
            Assert.True(Row(copy).IsRenaming);
        });
    }

    [CorpusFact]
    public void A_row_clicked_opens_that_vault_and_it_is_remembered()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Make("Playtest");
            Vaults.OpenVaultCommand.Execute(Row("Playtest"));

            Assert.Equal("Playtest", Vaults.OpenName);
            Assert.Equal("Opened Playtest.", Status);
            Assert.False(Vaults.IsPickerOpen);
            Assert.Equal("Playtest", _book.Settings.VaultNameSoftcore);
            Assert.Equal(0, _book.Store.Count());
        });
    }

    [CorpusFact]
    public void A_file_comes_in_as_a_folder_of_the_open_vault_the_folders_unticked_left_out()
    {
        if (!program.Ready(changes: true))
            return;

        string first = program.On(() =>
        {
            string was = First;
            Make("Playtest");
            Vaults.OpenVaultCommand.Execute(Row("Playtest"));
            return was;
        });
        Import(first);

        program.On(() =>
        {
            Assert.Equal("Import Shared", Vaults.ImportTitle);
            Assert.False(Vaults.IsPickerOpen);

            // As a vault, which it is to begin with: the whole file.
            Assert.True(Vaults.AsVault);
            Assert.Equal("6 / 6 items", Vaults.ImportCount);

            Vaults.ChooseFolderCommand.Execute(null);
            Assert.Equal("Folder in Playtest", Vaults.FolderChoice);
            Assert.Contains(Vaults.Nodes, node => node.Name == "Jewels" && !node.IsItems);
            Assert.Contains(Vaults.Nodes, node => node.Name == "Items in this folder" && node.IsItems);

            // Jewels unticked, and what is beneath it with it: its own item and Magic's.
            Vaults.Nodes.First(node => node.Name == "Jewels").IsTicked = false;
            Assert.False(Vaults.Nodes.First(node => node.Name == "Magic").IsTicked);
            Assert.Equal("4 / 6 items", Vaults.ImportCount);

            Vaults.ConfirmImportCommand.Execute(null);
            Assert.Equal("Imported as Shared: 4 items.", Status);
            Assert.False(Vaults.IsImportOpen);

            // A folder named for the file, without Jewels; and Playtest is still the vault open.
            var folders = _book.Store.Folders().Select(folder => folder.Name).ToList();
            Assert.Contains("Shared", folders);
            Assert.Contains("Small", folders);
            Assert.DoesNotContain("Jewels", folders);
            Assert.Equal(4, _book.Store.Count());
            Assert.Equal("Playtest", Vaults.OpenName);
        });
    }

    [CorpusFact]
    public void A_file_comes_in_as_a_vault_of_its_own_and_is_opened_the_one_open_before_still_in_the_list()
    {
        if (!program.Ready(changes: true))
            return;

        string first = program.On(() => First);
        Import(first);

        program.On(() =>
        {
            Vaults.ConfirmImportCommand.Execute(null);

            Assert.Equal("Imported and opened Shared.", Status);
            Assert.Equal("Shared", Vaults.OpenName);
            Assert.True(File.Exists(FileOf("Shared")));
            Assert.Equal(6, _book.Store.Count());

            Vaults.OpenPickerCommand.Execute(null);
            Assert.False(Row(first).IsOpen);
            Assert.True(Row("Shared").IsOpen);
        });
    }
}
