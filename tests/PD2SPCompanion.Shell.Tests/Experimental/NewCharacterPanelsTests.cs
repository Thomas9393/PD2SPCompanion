using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Core.Trade;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Experimental;

/// <summary>
/// The Extra page's Create and Armory Import panels: what the panel holds reaches the file, a new character shows in
/// the list, a name in use and a running game are refused, and Legit makes none. Import is handed the armory's answer
/// from the corpus' own copy, as Fetched: the link box is only ever given what is not a link, so nothing here goes
/// near the network.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class NewCharacterPanelsTests(Program program)
{
    private const string Made1 = "Newcomer", Made2 = "Armorer";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Experimental_R Extra => program.Get<VP_ViewModel_Experimental_R>();
    private CharacterCreationViewModel Create => Extra.Creation;
    private ArmoryImportViewModel Armory => Extra.Armory;

    private static string? ArmoryFile() =>
        Directory.Exists(Path.Combine(Program.Corpus, "ladder-s14", "softcore"))
            ? Directory.GetFiles(Path.Combine(Program.Corpus, "ladder-s14", "softcore"), "*.json").Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

    /// <summary>The panel's choices as a test finds them, and the characters it made gone.</summary>
    private void Leave()
    {
        program.On(() =>
        {
            Create.ClassIndex = 0;
            Create.LevelText = "1";
            Create.QuestsDone = false;
            Create.ModeIndex = 0;
            Create.NewName = "";
            Create.NameMissing = false;
            Armory.Lookup = "";
            Armory.NewName = "";
            Armory.Fetched = null;
        });
        Made.Gone(program, Made1, Made2);
    }

    [CorpusTheory]
    [InlineData("Sorceress", 37, false)]
    [InlineData("Druid", 1, true)]
    public void Create_writes_the_class_level_and_mode_the_panel_holds(string className, int level, bool hardcore)
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                Create.ClassIndex = Create.Classes.ToList().IndexOf(className);
                Create.LevelText = level.ToString();
                Create.ChooseModeCommand.Execute(hardcore ? "1" : "0");
                Assert.Equal(hardcore, Create.IsHardcore);
                Create.NewName = Made1;

                Create.CreateCommand.Execute(null);

                Assert.Equal($"{Made1} created. Open it from Characters.", Session.Messages.Text);
                var read = SaveSummary.TryRead(program.SaveOf(Made1));
                Assert.NotNull(read);
                Assert.Equal((Made1, level, className, hardcore), (read.Name, read.Level, read.ClassName, read.IsHardcore));

                // The name cannot be used twice; the choices stay for another like it.
                Assert.Equal("", Create.NewName);
                Assert.Equal(level.ToString(), Create.LevelText);

                Main.Home.CloseCommand.Execute(null);
            });

            Assert.True(program.Until(() => Main.Home.Characters.Any(card => card.Name == Made1)), $"{Made1} is not in the list");
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void The_level_steppers_stay_within_one_and_ninety_nine()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Create.LevelText = "1";
                Assert.False(Create.LowerLevelCommand.CanExecute(null));
                Create.RaiseLevelCommand.Execute(null);
                Assert.Equal("2", Create.LevelText);
                Create.LowerLevelCommand.Execute(null);
                Assert.Equal("1", Create.LevelText);

                Create.StepLevel(20);
                Assert.Equal("21", Create.LevelText);
                Create.StepLevel(-50);
                Assert.Equal("1", Create.LevelText);
                Create.LevelToEnd(up: true);
                Assert.Equal("99", Create.LevelText);
                Assert.False(Create.RaiseLevelCommand.CanExecute(null));
                Create.StepLevel(5);
                Assert.Equal("99", Create.LevelText);

                // Not a level: CREATE is dim, the steppers start from an end.
                Create.LevelText = "150";
                Assert.False(Create.CreateCommand.CanExecute(null));
                Create.LevelText = "";
                Assert.False(Create.CreateCommand.CanExecute(null));
                Create.RaiseLevelCommand.Execute(null);
                Assert.Equal("1", Create.LevelText);
                Assert.True(Create.CreateCommand.CanExecute(null));
            });
        }
        finally
        {
            program.On(() => Create.LevelText = "1");
        }
    }

    [CorpusFact]
    public void A_missing_name_a_bad_name_and_a_name_in_use_write_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                byte[] finder = File.ReadAllBytes(program.SaveOf(Program.Other));

                Create.NewName = "   ";
                Create.CreateCommand.Execute(null);
                Assert.True(Create.NameMissing);

                // A name typed: no longer said.
                Create.NewName = Program.Other;
                Assert.False(Create.NameMissing);

                Create.CreateCommand.Execute(null);
                Assert.Equal($"{Program.Other} already exists.", Session.Messages.Text);
                Assert.Equal(finder, File.ReadAllBytes(program.SaveOf(Program.Other)));

                Create.NewName = "a";
                Create.CreateCommand.Execute(null);
                Assert.Equal("Require 2 to 15 characters.", Session.Messages.Text);
                Assert.False(File.Exists(program.SaveOf("a")));
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void While_the_game_runs_neither_panel_writes()
    {
        if (!program.Ready(changes: true) || ArmoryFile() is not { } json)
            return;

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
        File.WriteAllText(program.GameFlag, "running");
        try
        {
            Assert.True(program.Until(() => Main.Sync?.IsGameRunning == true, 10000), "the game was not seen");
            program.On(() =>
            {
                Create.NewName = Made1;
                Create.CreateCommand.Execute(null);
                Assert.Equal("Require game closed to create.", Session.Status);
                Assert.False(File.Exists(program.SaveOf(Made1)));

                Armory.Fetched = new ArmoryImport.Armory(document.RootElement.Clone());
                Armory.NewName = Made2;
                Armory.ImportCommand.Execute(null);
                Assert.Equal("Require game closed to import.", Session.Status);
                Assert.False(File.Exists(program.SaveOf(Made2)));
            });
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
            Leave();
        }
    }

    [CorpusFact]
    public void Legit_makes_no_character_from_either_panel()
    {
        if (!program.Ready(changes: true) || ArmoryFile() is not { } json)
            return;

        using var mode = Made.Mode(program, "Legit");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
        try
        {
            program.On(() =>
            {
                Assert.False(Create.CanCreate);
                Assert.False(Armory.CanImport);

                Create.NewName = Made1;
                Create.CreateCommand.Execute(null);
                Assert.False(File.Exists(program.SaveOf(Made1)));

                Armory.Fetched = new ArmoryImport.Armory(document.RootElement.Clone());
                Armory.NewName = Made2;
                Armory.ImportCommand.Execute(null);
                Assert.False(File.Exists(program.SaveOf(Made2)));
            });
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void Import_writes_the_armorys_character_under_the_name_typed_and_starts_the_panel_again()
    {
        if (!program.Ready(changes: true) || ArmoryFile() is not { } json)
            return;

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
        try
        {
            program.On(() =>
            {
                var armory = new ArmoryImport.Armory(document.RootElement.Clone());
                Assert.False(Armory.ImportCommand.CanExecute(null));

                Armory.Fetched = armory;
                Assert.True(Armory.HasFetched);
                Assert.Equal($"Level {armory.Level} {armory.ClassName}", Armory.FetchedDescription);
                Assert.Equal($"{armory.Level}", Armory.FetchedLevel);
                Assert.Equal(armory.Name, Armory.FetchedCard!.Name);
                Assert.NotEqual("0", Armory.FetchedLife);

                // No name: nothing to press.
                Armory.NewName = "  ";
                Assert.False(Armory.ImportCommand.CanExecute(null));

                // A name in use: said, the answer kept to try another.
                Armory.NewName = Program.Other;
                Armory.ImportCommand.Execute(null);
                Assert.Equal($"{Program.Other} already exists.", Session.Messages.Text);
                Assert.True(Armory.HasFetched);

                Armory.NewName = Made2;
                Armory.ImportCommand.Execute(null);

                Assert.Contains($"{Made2} imported", Session.Messages.Text);
                var read = SaveSummary.TryRead(program.SaveOf(Made2));
                Assert.NotNull(read);
                Assert.Equal((Made2, armory.Level, armory.ClassName, armory.IsHardcore), (read.Name, read.Level, read.ClassName, read.IsHardcore));

                Assert.False(Armory.HasFetched);
                Assert.Equal("", Armory.NewName);
                Assert.Equal("", Armory.Lookup);
                Assert.Null(Armory.FetchedCard);

                Main.Home.CloseCommand.Execute(null);
            });

            Assert.True(program.Until(() => Main.Home.Characters.Any(card => card.Name == Made2)), $"{Made2} is not in the list");
        }
        finally
        {
            Leave();
        }
    }

    [CorpusFact]
    public void What_is_not_a_link_is_said_and_asks_the_armory_nothing()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Armory.Lookup = "just a name";
                Assert.Equal("Not an armory link.", Armory.Warning);
                Assert.False(Armory.IsBusy);
                Assert.True(Armory.ClearCommand.CanExecute(null));

                Armory.ClearCommand.Execute(null);
                Assert.Equal("", Armory.Lookup);
                Assert.Null(Armory.Warning);
                Assert.False(Armory.ClearCommand.CanExecute(null));
            });
        }
        finally
        {
            program.On(() => Armory.Lookup = "");
        }
    }
}
