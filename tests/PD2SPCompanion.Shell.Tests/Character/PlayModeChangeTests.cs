using System.ComponentModel;

using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// The Character page is kept while Settings changes the play mode: its level's steppers, its slider and its tooltips, the
/// skills' Respec and the skill cells follow the mode as it changes, not at the next edit.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PlayModeChangeTests(Program program)
{
    private VP_ViewModel_Char_L Stats => program.Get<VP_ViewModel_Char_L>();
    private VP_ViewModel_Char_R Skills => program.Get<VP_ViewModel_Char_R>();
    private EditorSettings Settings => program.Get<EditorSettings>();

    private static List<string?> Hear(INotifyPropertyChanged model, out Action stop)
    {
        var heard = new List<string?>();
        void Heard(object? _, PropertyChangedEventArgs changed) => heard.Add(changed.PropertyName);
        model.PropertyChanged += Heard;
        stop = () => model.PropertyChanged -= Heard;
        return heard;
    }

    [CorpusFact]
    public void The_levels_controls_are_told_of_the_mode_that_sets_them()
    {
        if (!program.Ready())
            return;

        using var casual = program.InMode("Casual");
        program.On(() =>
        {
            var heard = Hear(Stats, out var stop);
            try
            {
                Settings.PlayMode = "Legit";
                Assert.False(Stats.CanRaiseLevel);
                Assert.False(Stats.CanSlideProgress);

                foreach (string told in new[]
                         {
                             nameof(VP_ViewModel_Char_L.EditsLevel), nameof(VP_ViewModel_Char_L.CanRaiseLevel), nameof(VP_ViewModel_Char_L.CanLowerLevel),
                             nameof(VP_ViewModel_Char_L.CanSlideProgress), nameof(VP_ViewModel_Char_L.RaiseLevelTip), nameof(VP_ViewModel_Char_L.LowerLevelTip),
                             nameof(VP_ViewModel_Char_L.ShowsRespec),
                         })
                {
                    Assert.Contains(told, heard);
                }
            }
            finally
            {
                stop();
            }
        });
    }

    [CorpusFact]
    public void Respec_is_told_of_the_mode_and_refunds_nothing_in_one_that_edits_no_character()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        program.On(() =>
        {
            var heard = Hear(Skills, out var stop);
            try
            {
                Settings.PlayMode = "Legit";
                Assert.Contains(nameof(VP_ViewModel_Char_R.ShowsRespec), heard);
                Assert.False(Skills.ShowsRespec);

                int spent = Skills.Spent;
                if (Skills.RespecSkillsCommand.CanExecute(null))
                    Skills.RespecSkillsCommand.Execute(null);
                Assert.Equal(spent, Skills.Spent);
            }
            finally
            {
                stop();
            }
        });
    }

    [CorpusFact]
    public void A_look_the_rules_also_carry_builds_no_cell_again()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var rules = program.Get<PlayRules>();
            var heard = Hear(Skills, out var stop);
            bool was = rules.SocketsFaded;
            try
            {
                rules.SocketsFaded = !was;
                Assert.DoesNotContain(nameof(VP_ViewModel_Char_R.ShowsRespec), heard);
            }
            finally
            {
                rules.SocketsFaded = was;
                stop();
            }
        });
    }
}
