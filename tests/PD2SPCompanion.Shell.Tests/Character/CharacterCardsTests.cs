using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The Character page's three folding cards under the character card: Quests,
/// Waypoints, Map seed - open to begin with, each folding by its heading. Waypoints: Grant and Remove for the
/// act, with Ctrl for the difficulty and with Shift for all three; a reached main camp stays. Map seed: typed
/// and applied, rolled with the dice, kept under a name, chosen from the list, its bin asking twice - no
/// queue, no confirm.
/// <para>
/// What the cards decide, asked of them as the view asks (the view turns a held key into the reach, and Enter
/// in a box into its commit). That the cards lie in that order, that a heading's click folds what is drawn
/// and that the keys held reach the buttons are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CharacterCardsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Char_L Page => program.Get<VP_ViewModel_Char_L>();
    private WaypointsViewModel Waypoints => Page.Waypoints;
    private MapSeedViewModel MapSeed => Page.MapSeed;

    /// <summary>What the waypoints' card says on the left page's strip.</summary>
    private string Said => Session.Messages.Text;

    [CorpusFact]
    public void The_three_cards_are_open_to_begin_with_and_each_folds_by_its_heading()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Page.Shown();

                Assert.True(Page.QuestsOpen && Page.WaypointsOpen && Page.MapSeedOpen);
                Assert.Contains(Page.Quests.Rows, row => row.Name == "Den of Evil");
                Assert.Equal("Rogue Encampment", Waypoints.Points[0].Name);
                Assert.Equal(Session.MapSeed.ToString(), MapSeed.SeedText);

                Page.ToggleQuestsCommand.Execute(null);
                Assert.False(Page.QuestsOpen);
                Assert.True(Page.WaypointsOpen && Page.MapSeedOpen);

                Page.ToggleMapSeedCommand.Execute(null);
                Assert.False(Page.MapSeedOpen);
                Assert.True(Page.WaypointsOpen);

                Page.ToggleWaypointsCommand.Execute(null);
                Assert.False(Page.WaypointsOpen);

                Page.ToggleQuestsCommand.Execute(null);
                Assert.True(Page.QuestsOpen);
            });
        }
        finally
        {
            program.On(() =>
            {
                Page.QuestsOpen = Page.WaypointsOpen = Page.MapSeedOpen = true;
                Page.Hidden();
            });
        }
    }

    [CorpusFact]
    public void Grant_and_Remove_reach_the_act_the_difficulty_or_all_three_and_a_reached_camp_stays()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Free");
        try
        {
            // A new level 30 character holds Act I's camp only.
            program.On(() => Made.Character(program, "Carder", "Amazon", 30));
            Made.Open(program, "Carder");

            program.On(() =>
            {
                Page.Shown();
                Assert.Equal("Act I · 1 / 9", Waypoints.ActSummary);
                Assert.Equal("Grant Act I on Normal", Waypoints.GrantLabel);
                string nightmare = Waypoints.Summary.Split(" · ")[1];
                Assert.Matches(@"^Nightmare [01] / 39$", nightmare);

                // Grant: the act.
                Waypoints.EnableActCommand.Execute(null);
                Assert.Equal("Act I · 9 / 9", Waypoints.ActSummary);
                Assert.StartsWith("Normal 9 / 39 · ", Waypoints.Summary);
                Assert.Equal("Granted 8 waypoints in Act I on Normal.", Said);

                // With Ctrl: every act of the difficulty, the others untouched.
                Waypoints.EnableAll(WaypointsViewModel.ScopeDifficulty);
                Assert.StartsWith("Normal 39 / 39 · ", Waypoints.Summary);
                Assert.Equal(nightmare, Waypoints.Summary.Split(" · ")[1]);
                Assert.Equal("Granted 30 waypoints in Normal.", Said);

                // With Shift, Remove: everything, Act I's camp kept.
                Waypoints.RemoveAll(WaypointsViewModel.ScopeEverything);
                Assert.Equal("Act I · 1 / 9", Waypoints.ActSummary);
                Assert.StartsWith("Normal 1 / 39 · ", Waypoints.Summary);
                Assert.True(Waypoints.Points[0] is { Name: "Rogue Encampment", IsFound: true, IsFixed: true });

                Waypoints.RemoveActCommand.Execute(null);
                Assert.Equal("Nothing in Act I on Normal could be taken back.", Said);
            });
        }
        finally
        {
            program.On(Page.Hidden);
            Made.Gone(program, "Carder");
        }
    }

    [CorpusFact]
    public void A_map_seed_is_typed_kept_under_a_name_rolled_chosen_from_the_list_and_forgotten_on_the_second_press()
    {
        if (!program.Ready(changes: true))
            return;

        var settings = program.Get<EditorSettings>();
        using var mode = Made.Mode(program, "Free");
        try
        {
            program.On(() =>
            {
                Page.Shown();
                Assert.NotEqual(4242u, Session.MapSeed);

                // Typed, and Enter: on the character at once.
                MapSeed.SeedText = "4242";
                MapSeed.CommitSeed();
                Assert.Equal(4242u, Session.MapSeed);

                // Said: the undo step and the backup are named by it.
                Assert.Equal("Map seed 4242.", Session.Messages.Text);
                Assert.Equal("4242", MapSeed.Summary);

                // Named, and Enter: kept, and the heading names it.
                Assert.True(MapSeed.KeepCommand.CanExecute(null));
                MapSeed.NameText = "UI test seed";
                MapSeed.KeepCommand.Execute(null);
                Assert.Equal("4242 · UI test seed", MapSeed.Summary);
                Assert.True(MapSeed.Saved.Single(row => row.Remark == "UI test seed") is { Seed: 4242, IsCurrent: true });
                Assert.False(MapSeed.KeepCommand.CanExecute(null));

                // The dice: a new seed on.
                MapSeed.RollSeedCommand.Execute(null);
                Assert.NotEqual(4242u, Session.MapSeed);
                Assert.Matches(@"^\d+$", MapSeed.SeedText);
                Assert.Equal(Session.MapSeed.ToString(), MapSeed.SeedText);
                Assert.False(MapSeed.Saved.Single(row => row.Remark == "UI test seed").IsCurrent);

                // A kept seed clicked: the character goes onto it.
                MapSeed.ChooseCommand.Execute(MapSeed.Saved.Single(row => row.Remark == "UI test seed"));
                Assert.Equal(4242u, Session.MapSeed);
                Assert.Equal("4242 · UI test seed", MapSeed.Summary);

                // The bin arms on the first press and removes on the second, as every bin does.
                var kept = MapSeed.Saved.Single(row => row.Remark == "UI test seed");
                MapSeed.ForgetCommand.Execute(kept);
                Assert.True(kept.IsArmed);
                Assert.Contains(MapSeed.Saved, row => row.Remark == "UI test seed");

                MapSeed.ForgetCommand.Execute(kept);
                Assert.DoesNotContain(MapSeed.Saved, row => row.Remark == "UI test seed");
                Assert.DoesNotContain(settings.MapSeeds, note => note.Remark == "UI test seed");
                Assert.Equal("4242", MapSeed.Summary);
            });
        }
        finally
        {
            program.On(() =>
            {
                if (settings.MapSeeds.FirstOrDefault(note => note.Remark == "UI test seed") is { } left)
                {
                    settings.MapSeeds.Remove(left);
                    settings.Save();
                }

                Page.Hidden();
            });
        }
    }

    [CorpusFact]
    public void In_Legit_the_waypoints_and_the_map_seed_are_read_and_the_seed_can_still_be_kept_under_a_name()
    {
        if (!program.Ready(changes: true))
            return;

        // Shown in Legit too, read-only.
        var settings = program.Get<EditorSettings>();
        using var mode = Made.Mode(program, "Legit");
        try
        {
            program.On(() =>
            {
                Page.Shown();
                Assert.False(Page.Rules.EditsWaypoints);
                Assert.False(Page.Rules.EditsMapSeed);

                // The waypoints: read. Grant and a tick do nothing, and the summary stands.
                string held = Waypoints.Summary;
                Assert.NotEmpty(Waypoints.Points);
                Waypoints.EnableAll(WaypointsViewModel.ScopeEverything);
                Waypoints.RemoveAll(WaypointsViewModel.ScopeEverything);
                Assert.Equal(held, Waypoints.Summary);

                var row = Waypoints.Points.First(point => point.CanToggle);
                bool found = row.IsFound;
                row.IsFound = !found;
                Assert.Equal(found, row.IsFound);
                Assert.Equal(held, Waypoints.Summary);

                // The seed: read. Neither typed nor rolled nor gone onto.
                uint seed = Session.MapSeed;
                Assert.Equal(seed.ToString(), MapSeed.SeedText);
                MapSeed.SeedText = "4242";
                MapSeed.CommitSeed();
                Assert.Equal(seed, Session.MapSeed);
                Assert.Equal(seed.ToString(), MapSeed.SeedText);
                MapSeed.RollSeedCommand.Execute(null);
                Assert.Equal(seed, Session.MapSeed);

                // Kept under a name all the same: the list is the player's notes, not the character.
                MapSeed.NameText = "UI test legit seed";
                Assert.True(MapSeed.KeepCommand.CanExecute(null));
                MapSeed.KeepCommand.Execute(null);
                Assert.True(MapSeed.Saved.Single(kept => kept.Remark == "UI test legit seed") is { IsCurrent: true });
                Assert.Equal(seed, Session.MapSeed);
            });
        }
        finally
        {
            program.On(() =>
            {
                if (settings.MapSeeds.FirstOrDefault(note => note.Remark == "UI test legit seed") is { } left)
                {
                    settings.MapSeeds.Remove(left);
                    settings.Save();
                }
            });
        }
    }
}
