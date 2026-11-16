using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// What the gear adds, shown after a figure's own points: gear-added stats
/// written +xx in blue, strength 30 + 20 for example, the tooltip showing the base and the +xx from the
/// gear; in the skill tree, base point +xx. On the pad's own character, who wears gear
/// that gives attributes and skills. That the figures line up down the four rows is the view's, looked at.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GearPointsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Char_L Attributes => program.Get<VP_ViewModel_Char_L>();
    private VP_ViewModel_Char_R Skills => program.Get<VP_ViewModel_Char_R>();

    private void Shown(Action then) => program.On(() =>
    {
        Attributes.Shown();
        Skills.Shown();
        try
        {
            then();
        }
        finally
        {
            Attributes.Hidden();
            Skills.Hidden();
        }
    });

    [CorpusFact]
    public void An_attribute_says_what_the_gear_in_use_adds_after_its_own_points()
    {
        if (!program.Ready())
            return;

        Shown(() =>
        {
            // Counted as the requirements count gear: what is in use, its sockets and sets, per-level stats.
            var gear = Session.GearAttributes();
            Assert.Equal(AttributeEditor.Editable.Order(), gear.Keys.Order());
            Assert.Contains(gear.Values, value => value > 0);

            // Each row is told what the rows of its kind show: a figure is as wide as the longest of them.
            var geared = Attributes.Attributes.Where(row => row.HasBonus).ToList();
            var plain = Attributes.Attributes.Where(row => !row.HasBonus).ToList();
            Assert.All(geared, row => Assert.Equal(geared.Select(each => $"{each.Value}"), row.PointsOfAll));
            Assert.All(plain, row => Assert.Equal(plain.Select(each => $"{each.Value}"), row.PointsOfAll));
            Assert.All(geared, row => Assert.Equal(geared.Select(each => each.BonusNumber), row.GearOfAll));

            foreach (var row in Attributes.Attributes)
            {
                int bonus = gear[row.Stat];
                Assert.Equal(bonus, row.Bonus);
                Assert.Equal(bonus != 0, row.HasBonus);

                if (bonus > 0)
                {
                    Assert.Equal($"+{bonus}", row.BonusText);
                    Assert.Equal($"{row.Label} {row.Value + bonus}: {row.Value} base + {bonus} from items", row.ValueTip);
                }
                else if (bonus == 0)
                {
                    // Nothing to say: no figure after the points, and no tooltip.
                    Assert.Equal("", row.BonusText);
                    Assert.Null(row.ValueTip);
                }
            }
        });
    }

    [CorpusFact]
    public void A_skills_badge_says_the_levels_items_add_after_its_points()
    {
        if (!program.Ready())
            return;

        Shown(() =>
        {
            var skill = Skills.CurrentCells.First().Skill;
            byte classId = Session.OpenCard!.Save.ClassId;

            var both = new SkillCellViewModel(skill, 20, 90, classId, null, bonus: 3);
            Assert.True(both.ShowsBadge);
            Assert.Equal("20", both.PointsText);
            Assert.Equal(" +3", both.BonusText);

            // Its own points alone: as it was.
            var own = new SkillCellViewModel(skill, 20, 90, classId, null);
            Assert.True(own.ShowsBadge);
            Assert.Equal("", own.BonusText);

            // A skill only items give has a badge too.
            var given = new SkillCellViewModel(skill, 0, 90, classId, null, bonus: 3);
            Assert.True(given.ShowsBadge);
            Assert.Equal("0", given.PointsText);
            Assert.Equal(" +3", given.BonusText);

            // Neither: none.
            Assert.False(new SkillCellViewModel(skill, 0, 90, classId, null).ShowsBadge);

            // No hard point and only all skills: they wait, the cell grey.
            Assert.False(new SkillCellViewModel(skill, 0, 90, classId, null, waiting: 27).ShowsBadge);
        });
    }
}
