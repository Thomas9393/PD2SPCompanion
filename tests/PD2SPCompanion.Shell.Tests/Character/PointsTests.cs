using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The Character page's points in each play mode: the attributes' and the skills'
/// points to spend stepped by 5 and 20 and typed, in Free alone - Casual moves points but adds none, its box
/// read only; Legit changes nothing; each skill's own steppers and each attribute's, by 5, by 20 and by
/// every point, in Free and Casual; and the level's progress, moved in Free with the level kept, read
/// elsewhere.
/// <para>
/// What the pages decide, asked as the view asks: it is the view that turns Ctrl into 5, Shift into 20 and
/// both into every point, Enter in a box into its commit, and the slider's keys into a percent. That those
/// keys and the mouse reach the steppers, the tooltips, and that a slider is drawn in Free and a gauge
/// elsewhere, are the UI cases' to show. On "Stepper", a new level 30 Amazon made for each test.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PointsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Char_L Attributes => program.Get<VP_ViewModel_Char_L>();
    private VP_ViewModel_Char_R Skills => program.Get<VP_ViewModel_Char_R>();
    private PlayRules Rules => program.Get<PlayRules>();

    /// <summary>The first skill a point can go into, top of the tree - as it stands now: the cells are made anew after every point.</summary>
    private SkillCellViewModel FirstSkill => Skills.CurrentCells.OrderBy(cell => cell.GridRow).ThenBy(cell => cell.GridColumn).First();

    /// <summary>Stepper made and opened in a play mode, the Character page in front; disposed, all of it is gone again.</summary>
    private IDisposable Stepper(string mode) => new Staged(program, mode);

    private sealed class Staged : IDisposable
    {
        private readonly Program _program;
        private readonly IDisposable _mode;

        public Staged(Program program, string mode)
        {
            _program = program;
            _mode = Made.Mode(program, mode);
            try
            {
                program.On(() => Made.Character(program, "Stepper", "Amazon", 30));
                Made.Open(program, "Stepper");
                program.On(() =>
                {
                    program.Get<VP_ViewModel_Char_L>().Shown();
                    program.Get<VP_ViewModel_Char_R>().Shown();
                });
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _program.On(() =>
            {
                _program.Get<VP_ViewModel_Char_L>().Hidden();
                _program.Get<VP_ViewModel_Char_R>().Hidden();
            });
            Made.Gone(_program, "Stepper");
            _mode.Dispose();
        }
    }

    [CorpusFact]
    public void In_Free_the_points_to_spend_step_by_5_and_20_both_ways()
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper("Free");
        program.On(() =>
        {
            int stats = Attributes.Unspent, skills = Skills.Unspent;
            Assert.True(stats > 20 && skills > 20, $"a level 30 character has points to spend ({stats}, {skills})");
            Assert.True(Rules.AddsCharacterPoints);

            Skills.ChangeUnspent(5);
            Assert.Equal(skills + 5, Skills.Unspent);
            Skills.ChangeUnspent(20);
            Assert.Equal(skills + 25, Skills.Unspent);
            Assert.Equal($"{skills + 25}", Skills.UnspentText);
            Skills.ChangeUnspent(-5);
            Assert.Equal(skills + 20, Skills.Unspent);
            Skills.ChangeUnspent(-20);
            Assert.Equal(skills, Skills.Unspent);

            Attributes.ChangeUnspent(5);
            Assert.Equal(stats + 5, Attributes.Unspent);
            Assert.Equal($"{stats + 5}", Attributes.UnspentText);
            Attributes.ChangeUnspent(-5);
            Assert.Equal(stats, Attributes.Unspent);

            // The plain click's commands: one point.
            Skills.AddUnspentCommand.Execute(null);
            Assert.Equal(skills + 1, Skills.Unspent);
            Skills.RemoveUnspentCommand.Execute(null);
            Assert.Equal(skills, Skills.Unspent);
        });
    }

    [CorpusFact]
    public void In_Free_the_points_to_spend_box_takes_a_typed_number_and_goes_back_when_emptied()
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper("Free");
        program.On(() =>
        {
            int skills = Skills.Unspent;

            Skills.UnspentText = "7";
            Skills.CommitUnspent();
            Assert.Equal(7, Skills.Unspent);

            Skills.UnspentText = "";
            Skills.CommitUnspent();
            Assert.Equal(7, Skills.Unspent);
            Assert.Equal("7", Skills.UnspentText);

            Skills.UnspentText = $"{skills}";
            Skills.CommitUnspent();
            Assert.Equal(skills, Skills.Unspent);

            int stats = Attributes.Unspent;
            Attributes.UnspentText = "3";
            Attributes.CommitUnspent();
            Assert.Equal(3, Attributes.Unspent);
            Attributes.UnspentText = $"{stats}";
            Attributes.CommitUnspent();
            Assert.Equal(stats, Attributes.Unspent);
        });
    }

    [CorpusTheory]
    [InlineData("Casual")]
    [InlineData("Legit")]
    public void Outside_Free_no_points_are_added_and_the_points_to_spend_box_is_read_only(string mode)
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper(mode);
        program.On(() =>
        {
            int stats = Attributes.Unspent, skills = Skills.Unspent;
            Assert.True(stats > 20 && skills > 20);

            // No steppers beside the points to spend: the view shows them where points are added.
            Assert.False(Rules.AddsCharacterPoints);

            Skills.UnspentText = "99";
            Skills.CommitUnspent();
            Assert.Equal(skills, Skills.Unspent);
            Assert.Equal($"{skills}", Skills.UnspentText);

            Attributes.UnspentText = "99";
            Attributes.CommitUnspent();
            Assert.Equal(stats, Attributes.Unspent);
            Assert.Equal($"{stats}", Attributes.UnspentText);
        });
    }

    [CorpusFact]
    public void In_Free_the_levels_progress_moves_the_experience_and_keeps_the_level()
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper("Free");
        program.On(() =>
        {
            Assert.True(Attributes.EditsLevel);
            Assert.True(Attributes.CanSlideProgress);
            Assert.Equal(0, Attributes.LevelProgress);
            Assert.Equal("Level 30 Amazon", Attributes.CardDescription);
            long start = Session.Leveling!.Experience;

            Attributes.LevelProgress = 50;
            Assert.Equal(50, Attributes.LevelProgress);
            Assert.Equal("50%", Attributes.ProgressText);
            long half = Session.Leveling.Experience;
            Assert.True(half > start);
            Assert.Equal("Level 30 Amazon", Attributes.CardDescription);

            // As far as a level goes.
            Attributes.LevelProgress = 99;
            Assert.Equal(99, Attributes.LevelProgress);
            Assert.True(Session.Leveling.Experience > half);
            Assert.Equal("Level 30 Amazon", Attributes.CardDescription);

            // A step of 1.
            Attributes.LevelProgress = 98;
            Assert.Equal(98, Attributes.LevelProgress);
            Assert.Equal(30, Attributes.Level);
        });
    }

    [CorpusTheory]
    [InlineData("Casual")]
    [InlineData("Legit")]
    public void Outside_Free_the_levels_progress_is_a_gauge(string mode)
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper(mode);
        program.On(() =>
        {
            Assert.False(Attributes.EditsLevel);
            Assert.False(Attributes.CanSlideProgress);
            Assert.False(Attributes.CanRaiseLevel || Attributes.CanLowerLevel);

            long experience = Session.Leveling!.Experience;
            Attributes.LevelProgress = 50;
            Assert.Equal(0, Attributes.LevelProgress);
            Assert.Equal(experience, Session.Leveling.Experience);

            Attributes.StepLevel(5);
            Assert.Equal(30, Attributes.Level);
        });
    }

    [CorpusTheory]
    [InlineData("Free")]
    [InlineData("Casual")]
    public void A_skills_own_steppers_take_from_the_points_to_spend_and_give_back_to_them(string mode)
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper(mode);
        program.On(() =>
        {
            int skills = Skills.Unspent;
            Assert.True(Skills.EditsSkills);
            string name = FirstSkill.Name;
            Assert.True(FirstSkill.CanAdd);

            Skills.AddPoints(FirstSkill, 5);
            Assert.Equal(skills - 5, Skills.Unspent);
            Assert.Equal(5, FirstSkill.Points);
            Assert.Equal($"{name} +5.", Session.Status);

            // Twenty more asked: filled to its 20.
            Skills.AddPoints(FirstSkill, 20);
            Assert.Equal(skills - 20, Skills.Unspent);
            Assert.Equal(20, FirstSkill.Points);
            Assert.False(FirstSkill.CanAdd);

            Skills.RemovePoints(FirstSkill, 5);
            Assert.Equal(skills - 15, Skills.Unspent);
            Assert.Equal($"{name} -5.", Session.Status);

            // Twenty asked back: the rest.
            Skills.RemovePoints(FirstSkill, 20);
            Assert.Equal(skills, Skills.Unspent);
            Assert.Equal(0, FirstSkill.Points);

            // Both keys: every point there is, in and out again.
            Skills.AddPoints(FirstSkill, AttributeRowViewModel.EveryPoint);
            Assert.Equal(20, FirstSkill.Points);
            Skills.RemovePoints(FirstSkill, AttributeRowViewModel.EveryPoint);
            Assert.Equal(skills, Skills.Unspent);

            Assert.Contains("Ctrl + Shift + LMB: all", FirstSkill.AddTip);
        });
    }

    [CorpusTheory]
    [InlineData("Free")]
    [InlineData("Casual")]
    public void An_attributes_own_steppers_invest_and_refund_by_5_and_by_every_point(string mode)
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper(mode);
        program.On(() =>
        {
            int stats = Attributes.Unspent;
            var row = Attributes.Attributes[0];
            int value = row.Value;

            row.Invest(5);
            Assert.Equal(stats - 5, Attributes.Unspent);
            Assert.Equal(value + 5, row.Value);

            row.Refund(5);
            Assert.Equal(stats, Attributes.Unspent);
            Assert.Equal(value, row.Value);

            // Both keys: every point left, and every one back.
            row.Invest(AttributeRowViewModel.EveryPoint);
            Assert.Equal(0, Attributes.Unspent);
            Assert.Equal(value + stats, row.Value);
            Assert.False(row.CanRaise);

            row.Refund(AttributeRowViewModel.EveryPoint);
            Assert.Equal(stats, Attributes.Unspent);
            Assert.Equal(value, row.Value);
            Assert.False(row.CanLower);
        });
    }

    [CorpusFact]
    public void In_Legit_no_point_moves()
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper("Legit");
        program.On(() =>
        {
            int skills = Skills.Unspent;

            // No skill steppers and no attribute steppers: the view shows them where the character is edited.
            Assert.False(Rules.EditsCharacter);
            Assert.False(Skills.EditsSkills);
            Assert.False(Skills.ShowsRespec || Attributes.ShowsRespec);

            Skills.AddPoints(FirstSkill, 5);
            Assert.Equal(skills, Skills.Unspent);
            Assert.Equal(0, FirstSkill.Points);
        });
    }

    [CorpusFact]
    public void An_edit_of_the_character_says_what_it_did_so_its_undo_step_is_named_by_it()
    {
        if (!program.Ready(changes: true))
            return;

        // The autosave names the undo step and the backup by what the strip last said: an edit that said nothing
        // was recorded under the act before it.
        using var staged = Stepper("Free");
        program.On(() =>
        {
            var row = Attributes.Attributes[0];
            row.Invest(1);
            Assert.Equal($"{row.Label} {row.Value}.", Session.Status);

            row.Refund(1);
            Assert.Equal($"{row.Label} {row.Value}.", Session.Status);

            Attributes.StepLevel(1);
            Assert.Equal($"Level {Attributes.Level}.", Session.Status);

            Attributes.ChangeUnspent(1);
            Assert.Equal($"Attribute points to spend {Attributes.Unspent}.", Session.Status);

            Skills.ChangeUnspent(1);
            Assert.Equal($"Skill points to spend {Skills.Unspent}.", Session.Status);
        });
    }

    /// <summary>
    /// A dimmed stepper or respec says why, on a line under its name and without its key lines:
    /// a fresh level 30 has put no point in, and with its points to spend taken away has
    /// none to put in nor a level's worth to give back.
    /// </summary>
    [CorpusFact]
    public void A_dimmed_stepper_says_why_under_its_name()
    {
        if (!program.Ready(changes: true))
            return;

        using var staged = Stepper("Free");
        program.On(() =>
        {
            string line = Environment.NewLine;
            var strength = Attributes.Attributes.First(row => row.Stat == "strength");

            // Nothing spent: no point to take back, nothing to refund.
            Assert.False(strength.CanLower);
            Assert.Equal($"Remove point{line}Strength at class minimum {strength.Minimum}.", strength.LowerTip);
            Assert.Equal($"Refund attribute points{line}No attribute points spent.", Attributes.RespecTip);
            Assert.Equal($"Refund skill points{line}No skill points spent.", Skills.RespecSkillsTip);
            Assert.Equal($"Remove point{line}No points in {FirstSkill.Name}.", FirstSkill.RemoveTip);

            // Lit, a stepper lists its keys and gives no reason.
            Assert.True(strength.CanRaise);
            Assert.Equal(string.Join(line, "Add point", "Ctrl + LMB: 5 points", "Shift + LMB: 20 points", "Ctrl + Shift + LMB: all"), strength.RaiseTip);
            Assert.StartsWith("Remove points" + line + "Ctrl + LMB", Attributes.RemoveUnspentTip);
            Assert.StartsWith("Lower level" + line + "Ctrl + LMB", Attributes.LowerLevelTip);

            // The points to spend taken away: none to put in, none to remove, no level's worth to give back.
            Attributes.ChangeUnspent(-Attributes.Unspent);
            Skills.ChangeUnspent(-Skills.Unspent);
            Assert.Equal($"Add point{line}No attribute points to spend.", strength.RaiseTip);
            Assert.Equal($"Remove points{line}No attribute points to spend.", Attributes.RemoveUnspentTip);
            Assert.Equal($"Remove points{line}No skill points to spend.", Skills.RemoveUnspentTip);
            Assert.Equal($"Add point{line}No skill points to spend.", FirstSkill.AddTip);
            Assert.False(Attributes.CanLowerLevel);
            Assert.Equal($"Lower level{line}Require 5 attribute points and 1 skill point unspent.", Attributes.LowerLevelTip);
        });
    }
}
