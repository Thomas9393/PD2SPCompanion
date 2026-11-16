using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// A character renamed from its card, carried through: the files (CharacterRename's, and the Core's tests'), then what
/// the home page follows up with - the list, the card still selected, the trades of the open vault and of another vault
/// of the season, the page locks kept by the name. Asked on "Finder", the character that is not open, as the tick of
/// the name box asks. That the box is drawn and its tick takes a click is a window's to show.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RenameCharacterTests(Program program)
{
    private const string NewName = "Seeker", Elsewhere = "Second Vault";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Default_L HomePage => Main.Home;
    private VaultService Vaults => program.Get<VaultService>();

    private static TradeRecord Trade(string character) =>
        new(DateTimeOffset.UtcNow, character, "buy", null, null, null, null, "Ring", "1", "1", "Casual");

    /// <summary>Nothing open and Finder's card chosen, as a player finds it on the home page.</summary>
    private void ChooseFinder()
    {
        program.On(() => HomePage.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Session.IsLoaded && HomePage.Characters.Any(card => card.Name == Program.Other)), "Finder is not in the list");
        program.On(() => HomePage.SelectedIndex = HomePage.Characters.ToList().FindIndex(card => card.Name == Program.Other));
    }

    [CorpusFact]
    public void Renamed_the_file_the_list_the_selection_and_every_vaults_trades_follow()
    {
        if (!program.Ready(changes: true))
            return;

        string lockKey = $"character:{Program.Other}:Cube", lockMoved = $"character:{NewName}:Cube";
        var settings = program.On(() => program.Get<EditorSettings>());
        try
        {
            ChooseFinder();
            program.On(() =>
            {
                // A trade by Finder in the open vault and in another of the season; one by another in each stays his.
                Vaults.Store!.RecordTrades([Trade(Program.Other), Trade(Program.Open)]);
                VaultLibrary.CreateVault(Vaults.Season!, false, Elsewhere, null, Vaults.Store.StatIdBits);
                using (var other = VaultLibrary.OpenVault(Vaults.Season!, false, Elsewhere, Vaults.Store.StatIdBits))
                    other.RecordTrades([Trade(Program.Other), Trade(Program.Open)]);

                settings.LockedPages.Add(lockKey);

                HomePage.ToggleRenameCommand.Execute(null);
                Assert.True(HomePage.IsRenaming);
                Assert.False(HomePage.IsCloning);
                Assert.Equal(Program.Other, HomePage.NewName);

                HomePage.NewName = "  " + NewName + " ";
                HomePage.ConfirmNameCommand.Execute(null);

                Assert.Equal($"Renamed {Program.Other} to {NewName}.", Session.Status);
                Assert.False(HomePage.IsRenaming);
                Assert.Equal("", HomePage.NewName);

                // The files.
                Assert.False(File.Exists(program.SaveOf(Program.Other)), "the old save is still there");
                var read = SaveSummary.TryRead(program.SaveOf(NewName));
                Assert.NotNull(read);
                Assert.Equal(NewName, read.Name);

                // The list, with the card still selected.
                Assert.DoesNotContain(HomePage.Characters, card => card.Name == Program.Other);
                Assert.Contains(HomePage.Characters, card => card.Name == NewName);
                Assert.Equal(NewName, HomePage.Selected?.Name);

                // The open vault's records and the other's; a trade of someone else's is not touched.
                Assert.Equal(1, Vaults.Store!.Trades().Count(trade => trade.Character == NewName));
                Assert.DoesNotContain(Vaults.Store.Trades(), trade => trade.Character == Program.Other);
                Assert.Contains(Vaults.Store.Trades(), trade => trade.Character == Program.Open);
                using (var other = VaultLibrary.OpenVault(Vaults.Season!, false, Elsewhere, Vaults.Store.StatIdBits))
                {
                    var trades = other.Trades();
                    Assert.Equal([NewName, Program.Open], trades.Select(trade => trade.Character).Order(StringComparer.Ordinal).Reverse());
                }

                // The page locks are kept by the name.
                Assert.False(settings.IsPageLocked(lockKey));
                Assert.True(settings.IsPageLocked(lockMoved));
            });
        }
        finally
        {
            program.On(() =>
            {
                settings.LockedPages.Remove(lockKey);
                settings.LockedPages.Remove(lockMoved);
            });
        }
    }

    [CorpusFact]
    public void A_name_taken_or_bad_is_said_and_nothing_moves()
    {
        if (!program.Ready(changes: true))
            return;

        ChooseFinder();
        program.On(() =>
        {
            byte[] before = File.ReadAllBytes(program.SaveOf(Program.Other));

            void Refused(string typed, string said)
            {
                HomePage.ToggleRenameCommand.Execute(null);
                HomePage.NewName = typed;
                HomePage.ConfirmNameCommand.Execute(null);

                Assert.Equal(said, Session.Status);
                Assert.True(HomePage.IsRenaming, "the box went: the name cannot be corrected");
                Assert.Equal(before, File.ReadAllBytes(program.SaveOf(Program.Other)));
                Assert.Equal(Program.Other, HomePage.Selected?.Name);

                HomePage.CancelRenameCommand.Execute(null);
            }

            Refused(Program.Open, $"{Program.Open} already exists.");
            Refused("a", "Require 2 to 15 characters.");
            Refused(Program.Other, "Same name.");
            Assert.False(File.Exists(program.SaveOf("a")));
        });
    }

    [CorpusFact]
    public void While_the_game_runs_the_rename_is_refused_and_nothing_moves()
    {
        if (!program.Ready(changes: true))
            return;

        ChooseFinder();
        program.On(() =>
        {
            HomePage.ToggleRenameCommand.Execute(null);
            HomePage.NewName = NewName;
        });

        File.WriteAllText(program.GameFlag, "running");
        try
        {
            Assert.True(program.Until(() => Main.Sync?.IsGameRunning == true, 10000), "the game was not seen");
            program.On(() =>
            {
                byte[] before = File.ReadAllBytes(program.SaveOf(Program.Other));

                HomePage.ConfirmNameCommand.Execute(null);

                Assert.Equal("Require game closed to rename.", Session.Status);
                Assert.False(File.Exists(program.SaveOf(NewName)), "renamed under the game");
                Assert.Equal(before, File.ReadAllBytes(program.SaveOf(Program.Other)));
                HomePage.CancelRenameCommand.Execute(null);
            });
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
        }
    }
}
