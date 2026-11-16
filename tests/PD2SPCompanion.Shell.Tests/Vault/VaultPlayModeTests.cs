using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The play mode a vault keeps.
/// A vault that keeps none takes the mode it is first opened under, unasked. The play
/// mode changed while a vault is open, and a vault opened under another mode, both ask - two answers, each a
/// click: use the vault's mode, or change the vault. Custom modes are kept under names: one saved with no
/// name is "Custom - 1", the vault carries its rules, one edited under its own name is taken by the open vault
/// unasked - and a vault that still carries the rules of before brings them in as "1 (OLD)".
/// <para>
/// The whole of it is deciding - the service's, the settings' and the Custom panel's - and is proven here, the
/// keeping switched on for the test (PD2SPC_VAULT_MODE, which every other test has off). That neither Enter
/// nor Escape answers the question in the window is the UI case's to show (units/vault-play-mode.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultPlayModeTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private EditorSettings Settings => _book.Settings;
    private ConfirmService Question => program.Get<ConfirmService>();
    private PlayModeViewModel Mode => program.Get<VP_ViewModel_Settings_R>().PlayMode;
    private VaultManagerViewModel Vaults => program.Get<VP_ViewModel_Settings_R>().Vaults!;

    /// <summary>The mode's first vault, open as a test begins.</summary>
    private string First => _book.Vaults.VaultName;

    /// <summary>The play mode the open vault keeps; "" for none.</summary>
    private string Kept => _book.Store.About(VaultLibrary.PlayModeKey) ?? "";

    /// <summary>The play mode a vault of the mode keeps, open or not, as the list of vaults reads it.</summary>
    private string KeptBy(string vault) => _book.Vaults.Vaults().First(each => each.Name == vault).PlayMode;

    private PolicyRow Switch(string title) => Mode.Rows.First(row => row.Title == title);

    /// <summary>
    /// The program as it starts under a play mode, the first vault open and keeping none: the mode chosen with
    /// the keeping still off, then the keeping on and the vault looked at, as it is once it has been opened.
    /// </summary>
    private void StartUnder(PlayPolicy policy)
    {
        program.On(() =>
        {
            Assert.Equal("", Kept);
            Settings.ChoosePolicy(policy);
            Assert.Equal("", Kept);

            Environment.SetEnvironmentVariable("PD2SPC_VAULT_MODE", null);
            program.Get<VaultPlayMode>().Check();
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_VAULT_MODE", "off");
        if (program.Available)
        {
            program.On(() =>
            {
                // A question left up by a test that failed: CHANGE VAULT writes to the vault only, which is put back.
                if (Question.IsAsking)
                    Question.RejectCommand.Execute(null);

                Mode.CloseCustomCommand.Execute(null);
                Vaults.ClosePickerCommand.Execute(null);
            });
        }

        _book.PutAway();
    }

    [CorpusFact]
    public void A_vault_that_keeps_no_mode_takes_the_mode_it_is_opened_under_unasked()
    {
        if (!program.Ready(changes: true))
            return;

        StartUnder(PlayPolicy.Legit);
        program.On(() =>
        {
            Assert.Equal("Legit", Kept);
            Assert.False(Question.IsAsking);
            Assert.EndsWith("· Legit", Vaults.Summary);
        });
    }

    [CorpusFact]
    public void The_play_mode_changed_while_a_vault_is_open_asks_and_each_answer_is_a_click()
    {
        if (!program.Ready(changes: true))
            return;

        StartUnder(PlayPolicy.Legit);
        program.On(() =>
        {
            Mode.PickModeCommand.Execute(PlayPolicy.FreeName);

            Assert.True(Question.IsAsking);
            Assert.Equal($"{First} is a Legit vault. Play mode is Free.", Question.Message);
            Assert.Equal("USE LEGIT", Question.ConfirmLabel);
            Assert.Equal("CHANGE VAULT", Question.CancelLabel);

            // Two answers and no "no": a key pressed for something else must not pick one.
            Assert.True(Question.NeedsClick);

            Question.AcceptCommand.Execute(null);
            Assert.False(Question.IsAsking);
            Assert.Equal(PlayPolicy.LegitName, Settings.PlayMode);
            Assert.Equal("Play mode: Legit.", _book.Session.Status);
            Assert.Equal("Legit", Kept);

            // The other answer: the vault changed to this mode.
            Mode.PickModeCommand.Execute(PlayPolicy.FreeName);
            Assert.True(Question.IsAsking);
            Question.RejectCommand.Execute(null);

            Assert.False(Question.IsAsking);
            Assert.Equal("Free", Kept);
            Assert.Equal(PlayPolicy.FreeName, Settings.PlayMode);
            Assert.Equal($"{First}: Free vault.", _book.Session.Status);
            Assert.EndsWith("· Free", Vaults.Summary);
        });
    }

    [CorpusFact]
    public void A_custom_mode_saved_with_no_name_is_Custom_1_and_the_vault_asked_about_is_changed_to_it()
    {
        if (!program.Ready(changes: true))
            return;

        StartUnder(PlayPolicy.Free);
        program.On(() =>
        {
            Assert.Equal("Free", Kept);

            // The panel opens on the mode itself, nothing changed; a row changed is counted against it.
            Mode.OpenCustomCommand.Execute(null);
            Assert.Equal("Free", Mode.ChangesText);
            Switch("Waypoints").IsOn = false;
            Assert.Equal("Free, 1 change", Mode.ChangesText);

            Mode.ConfirmCustomCommand.Execute(null);
            Assert.True(Question.IsAsking);
            Assert.Equal($"{First} is a Free vault. Play mode is Custom - 1.", Question.Message);
            Question.RejectCommand.Execute(null);

            Assert.Equal("Custom - 1", Kept);
            Assert.EndsWith("· Custom - 1", Vaults.Summary);
            Assert.Equal("CUSTOM - 1", Mode.CustomButton);
            Assert.Equal("1", Assert.Single(Settings.CustomPresets).Name);
            Assert.Equal("1", Settings.CustomName);
        });
    }

    [CorpusFact]
    public void A_new_vault_takes_the_mode_unasked_one_edited_in_place_is_taken_unasked_and_the_rules_of_before_come_in_as_OLD()
    {
        if (!program.Ready(changes: true))
            return;

        // Custom - 1, the first vault's mode: Free but for the waypoints.
        StartUnder(PlayPolicy.Free with { Waypoints = false });
        string first = program.On(() =>
        {
            Assert.Equal("Custom - 1", Kept);

            Vaults.OpenPickerCommand.Execute(null);
            Vaults.StartNewCommand.Execute(null);
            Vaults.NewName = "Playtest";
            Vaults.CommitNewCommand.Execute(null);

            string was = First;
            Vaults.OpenVaultCommand.Execute(Vaults.Rows.First(row => row.Name == "Playtest"));
            Assert.Equal("Opened Playtest.", _book.Session.Status);
            return was;
        });

        // A new vault keeps no mode: opened, it takes this one, and asks nothing.
        _book.Until(() => Kept == "Custom - 1", "the new vault did not take the play mode");
        program.On(() =>
        {
            Assert.False(Question.IsAsking);

            // The custom mode edited under its own name: the open vault takes the new rules, unasked.
            Mode.OpenCustomCommand.Execute(null);
            Switch("Map seed").IsOn = !Switch("Map seed").IsOn;
            Mode.ConfirmCustomCommand.Execute(null);

            Assert.False(Question.IsAsking);
            Assert.Equal("1", Assert.Single(Settings.CustomPresets).Name);
            Assert.Equal("Custom - 1", Kept);

            // The first vault still carries the rules of before under that name.
            Vaults.OpenPickerCommand.Execute(null);
            Vaults.OpenVaultCommand.Execute(Vaults.Rows.First(row => row.Name == first));
        });

        _book.Until(() => Question.IsAsking, "the vault with the rules of before was not asked about");
        program.On(() =>
        {
            Assert.Equal($"{first} is a Custom - 1 (OLD) vault. Play mode is Custom - 1.", Question.Message);
            Assert.Equal("USE CUSTOM - 1 (OLD)", Question.ConfirmLabel);

            Question.AcceptCommand.Execute(null);
            Assert.Equal("1 (OLD)", Settings.CustomName);
            Assert.Equal(2, Settings.CustomPresets.Count);

            // Each vault keeps its own.
            Assert.Equal("Custom - 1 (OLD)", KeptBy(first));
            Assert.Equal("Custom - 1", KeptBy("Playtest"));
        });
    }
}
