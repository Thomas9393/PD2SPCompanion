using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Window;

/// <summary>
/// Back from the game: the game's start closes the open character, saying so; loaded
/// again once the game has closed, the character is back on the page and the bookmarks it was left on - and
/// the Items page says what the save now holds, a mercenary hired in the game among it.
/// <para>
/// The game is pretended (the program's GameFlag), and the save swapped while the character is closed, as
/// the game would have written it. What only a window shows - that the Items page, kept hidden meanwhile,
/// lays the new mercenary's section out and scrolls to it (PageDeck.Reclaim) - is the UI case's.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ResumeAfterGameTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    /// <summary>The saves of one character, before and after a mercenary was hired in game.</summary>
    private static string Fixture(string name) => Path.Combine(Program.Corpus, "ui-pad", "fixtures", name);

    /// <summary>The game started: seen within the two seconds between looks, and the character closed for it.</summary>
    private void GameStarts()
    {
        File.WriteAllText(program.GameFlag, "running");
        Assert.True(program.Until(() => Main.Sync?.IsGameRunning == true && !Session.IsLoaded, 10000), "the game was not seen");
    }

    private void GameEnds()
    {
        File.Delete(program.GameFlag);
        Assert.True(program.Until(() => Main.Sync?.IsGameRunning != true, 10000), "the game's end was not seen");
    }

    private void Load(string name)
    {
        program.On(() => Main.Home.SwitchTo(program.SaveOf(name)));
        Assert.True(program.Until(() => Session.IsLoaded && Session.CharacterName == name, 15000), $"{name} was not loaded");
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void The_character_the_game_closed_loaded_again_is_back_on_the_page_and_bookmarks_it_was_left_on()
    {
        if (!program.Ready())
            return;

        try
        {
            // On the Items page, the grail beside the inventory.
            program.On(() => Main.ShowRightCommand.Execute(MainViewModel.GrailMark));
            program.On(() => Assert.True(Main.Button_Inventory_isChecked && Main.IsLeftInventory && Main.IsRightGrail));

            GameStarts();
            Assert.True(program.Until(() => !Main.Button_Inventory_isChecked && ReferenceEquals(Main.CurrentPage_L, Main.Home)), "the home page was not shown");
            program.On(() =>
            {
                Assert.Equal($"Game started. {Program.Open} closed; reload after game closes.", Session.Status);
                Assert.False(Main.IsRightGrail);
            });

            GameEnds();
            program.On(() => Assert.Equal($"Game closed. Load {Program.Open} to continue.", Session.Status));

            Load(Program.Open);
            Assert.True(program.Until(() => Main.Button_Inventory_isChecked && Main.IsRightGrail), "the Items page was not shown again with the grail beside it");
            program.On(() =>
            {
                Assert.True(Main.IsLeftInventory);
                Assert.Same(Page, Main.CurrentPage_L);
            });
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
            program.On(() => Main.ShowRightCommand.Execute(MainViewModel.EditMark));
        }
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void A_mercenary_hired_in_the_game_is_on_the_Items_page_the_character_comes_back_to()
    {
        if (!program.Ready(changes: true) || !File.Exists(Fixture("GoldFind-no-mercenary.d2s")))
            return;

        const string name = "GoldFind";
        try
        {
            File.Copy(Fixture("GoldFind-no-mercenary.d2s"), program.SaveOf(name), overwrite: true);
            Load(name);
            program.On(() =>
            {
                Main.ShowPageByKey("Inventory_L");
                Assert.True(Main.Button_Inventory_isChecked);
                Assert.False(Page.HasMerc);
                Assert.Equal("Mercenary  (none hired)", Page.MercHeader);
            });

            // The game starts and closes the character; a mercenary is hired in it; the game ends.
            GameStarts();
            File.Copy(Fixture("GoldFind-mercenary.d2s"), program.SaveOf(name), overwrite: true);
            GameEnds();

            Load(name);
            Assert.True(program.Until(() => Main.Button_Inventory_isChecked && Page.HasMerc), "the Items page was not shown again with the mercenary on it");
            program.On(() =>
            {
                Assert.Same(Page, Main.CurrentPage_L);
                Assert.StartsWith("Mercenary", Page.MercHeader);
                Assert.DoesNotContain("none hired", Page.MercHeader);
                Assert.NotEmpty(Session.MercEquipped);
            });
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);

            // The pad's own character again before the save is taken away; the next test's Ready puts the folder back.
            if (program.On(() => Session.IsLoaded && Session.CharacterName == name))
                Load(Program.Open);
            File.Delete(program.SaveOf(name));
        }
    }

    /// <summary>
    /// One click a press (the click "may play twice and sometimes triple" as a character came back
    /// from the game): OPEN pressed is one click, and the page and bookmarks the character is put back on, the
    /// program's own doing, are none. Nor does a page gone to by the window's search add one to its row's.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void The_character_put_back_on_its_page_is_one_click_the_press_of_OPEN()
    {
        if (!program.Ready())
            return;

        var sound = (PD2SPCompanion.Shell.Services.Generic.SFXManager)program.Get<PD2SPCompanion.Shell.Services.Generic.ISFXManager>();
        try
        {
            program.On(() => Main.ShowRightCommand.Execute(MainViewModel.GrailMark));
            GameStarts();
            GameEnds();

            int before = program.On(() => sound.Presses);
            program.On(() => Main.Home.LoadCommand.Execute(null));
            Assert.True(program.Until(() => Session.IsLoaded && Main.Button_Inventory_isChecked && Main.IsRightGrail, 15000),
                "the character was not put back on the Items page with the grail beside it");
            Assert.Equal(1, program.On(() => sound.Presses) - before);

            // A page of the window's search gone to: the program's, no click of its own.
            before = program.On(() => sound.Presses);
            program.On(() =>
            {
                Assert.True(Main.ShowPageByKey("Char_L"));
                Main.PageTargets().First(target => target.Name == "Trade").Go();
            });
            Assert.Equal(0, program.On(() => sound.Presses) - before);
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
            program.On(() => Main.ShowRightCommand.Execute(MainViewModel.EditMark));
        }
    }
}
