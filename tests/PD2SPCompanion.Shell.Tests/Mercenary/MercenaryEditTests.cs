using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Data;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Mercenary;

/// <summary>
/// The mercenary, edited within its type (MercenaryEdit): the three lines over its slots,
/// the popup the pencil opens, the level held to the character's own outside Free, another variant written as
/// the one hired in Normal, another name, the undo, a dead one's badge and Revive, and what Legit leaves of it.
/// The pad's character is level 86 and its Ascendant - Dark, hired in Hell - is level 86 too.
/// <para>
/// What the page decides, asked of it as the popup's boxes and buttons ask, and read back from the page, the
/// strip and the file. That the pencil is drawn after the name and Revive after the badge, that the popup opens
/// with its cross in its header and that keys typed in the level's box arrive, are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MercenaryEditTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    /// <summary>What the file says of the mercenary: its level, its row of the hireling table, its name's place, and the line the command line tool prints.</summary>
    private (int Level, int Type, int NameId, string Line) Hired()
    {
        var save = Pad.File(program, Program.Open);
        var profile = MercenaryProfile.Read(save, Session.EnsureGame());
        return (profile.Level, save.Mercenary.TypeId, save.Mercenary.NameId, profile.Detail);
    }

    /// <summary>The mercenary dead in the file, as the game leaves one, and the file read again.</summary>
    private void Dead()
    {
        Pad.SavedInGame(program, save => save.Mercenary.IsDead = 1);
        Assert.True(program.Until(() => Page.MercIsDead), "the dead mercenary was not read");
    }

    [CorpusFact]
    public void Three_lines_say_its_name_its_level_and_kind_and_its_variant_with_no_difficulty()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var file = Hired();
            Assert.Equal((86, 34), (file.Level, file.Type));
            Assert.Equal("Dark (Amplify Damage), Hell, level 86", file.Line);

            Assert.NotEmpty(Session.MercenaryName);
            Assert.Equal(Session.MercenaryName, Page.MercName);
            Assert.Equal("Level 86 Ascendant", Page.MercLevelLine);
            Assert.Equal("Dark (Amplify Damage)", Page.MercVariantName);
        });
    }

    [CorpusFact]
    public void The_pencil_opens_the_popup_on_what_the_mercenary_is_and_its_cross_shuts_it()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Assert.True(Page.EditsMerc);
                Assert.False(Page.IsMercEditOpen);

                Page.EditMercCommand.Execute(null);
                Assert.True(Page.IsMercEditOpen);
                Assert.True(Page.IsMercEditPresent);
                Assert.Equal("86", Page.MercLevelText);
                Assert.Equal(Session.MercenaryName, Page.MercNames[Page.MercNameIndex]);
                Assert.Equal("Dark (Amplify Damage)", Page.MercVariants[Page.MercVariantIndex]);

                // Alive: no Revive beside its level.
                Assert.False(Page.MercIsDead);
                Assert.False(Page.ShowsMercRevive);

                Page.CloseMercEditCommand.Execute(null);
                Assert.False(Page.IsMercEditOpen);
                Assert.True(Page.EditsMerc);
            });
        }
        finally
        {
            program.On(() => Page.IsMercEditOpen = false);
        }
    }

    [CorpusFact]
    public void In_Casual_the_level_is_held_to_the_characters_own()
    {
        if (!program.Ready(changes: true))
            return;

        Pad.In(program, "Casual", () => program.On(() =>
        {
            Assert.Equal(86, Page.MercLevelMost);
            Assert.Equal("Level 1-86, the character's own", Page.MercLevelHint);
            Assert.False(Page.CanRaiseMercLevel);
            Assert.True(Page.CanLowerMercLevel);

            Page.LowerMercLevelCommand.Execute(null);
            Assert.Equal("Mercenary level 85.", Session.Status);
            Assert.Equal("85", Page.MercLevelText);
            Assert.Equal("Level 85 Ascendant", Page.MercLevelLine);
            Assert.True(Page.CanRaiseMercLevel);
            Assert.Equal(85, Hired().Level);

            // Typed past the character's level: held to it.
            Page.MercLevelText = "95";
            Assert.Equal("86", Page.MercLevelText);
            Assert.Equal(86, Hired().Level);
        }));
    }

    [CorpusFact]
    public void In_Free_a_level_past_the_characters_is_taken_up_to_98_where_Raise_ends()
    {
        if (!program.Ready(changes: true))
            return;

        Pad.In(program, "Free", () => program.On(() =>
        {
            Assert.Equal(98, Page.MercLevelMost);
            Assert.Equal("Level 1-98", Page.MercLevelHint);
            Assert.True(Page.CanRaiseMercLevel);

            Page.MercLevelText = "97";
            Assert.Equal("Mercenary level 97.", Session.Status);
            Page.RaiseMercLevelCommand.Execute(null);

            Assert.Equal("Level 98 Ascendant", Page.MercLevelLine);
            Assert.False(Page.CanRaiseMercLevel);
            Assert.Equal(98, Hired().Level);
        }));
    }

    [CorpusFact]
    public void Another_variant_is_offered_once_by_its_name_and_written_as_the_one_hired_in_Normal()
    {
        if (!program.Ready(changes: true))
            return;

        Pad.In(program, "Free", () => program.On(() =>
        {
            var before = Hired();
            Page.MercLevelText = "98";

            Assert.Single(Page.MercVariants, variant => variant == "Light (Sanctuary)");
            Page.MercVariantIndex = Page.MercVariants.ToList().IndexOf("Light (Sanctuary)");

            Assert.Equal("Mercenary: Light (Sanctuary).", Session.Status);
            Assert.Equal("Light (Sanctuary)", Page.MercVariantName);
            Assert.Equal("Light (Sanctuary)", Page.MercVariants[Page.MercVariantIndex]);

            // Its level is kept, in that row's own experience; its name too.
            var now = Hired();
            Assert.Equal(31, now.Type);
            Assert.Equal("Light (Sanctuary), Normal, level 98", now.Line);
            Assert.Equal(before.NameId, now.NameId);
            Assert.True(Pad.ReadsBack(program, Program.Open), "the save does not read back");
        }));
    }

    [CorpusFact]
    public void Another_name_of_its_types_list_is_written_and_the_undo_takes_it_back()
    {
        if (!program.Ready(changes: true))
            return;

        var before = (Level: 0, Type: 0, NameId: 0, Line: "");
        string named = "";
        Pad.In(program, "Free", () =>
        {
            program.On(() =>
            {
                before = Hired();
                string was = Session.MercenaryName;
                int other = Enumerable.Range(0, Page.MercNames.Count).First(at => Page.MercNames[at] != was);
                named = Page.MercNames[other];

                Page.MercNameIndex = other;
                Assert.Equal($"Mercenary named {named}.", Session.Status);
                Assert.Equal(named, Page.MercName);
                Assert.Equal(other, Page.MercNameIndex);
                Assert.Equal(other, Hired().NameId);

                Assert.True(Main.Home.UndoCommand.CanExecute(null));
                Main.Home.UndoCommand.Execute(null);
            });

            Assert.True(program.Until(() => Page.MercName != named), "the undo did not take the name back");
            program.On(() =>
            {
                Assert.Equal(before, Hired());
                Assert.Equal(before.NameId, Page.MercNameIndex);
            });
        });
    }

    [CorpusFact]
    public void A_dead_one_wears_the_badge_beside_its_level_line_and_Revive_takes_both_away()
    {
        if (!program.Ready(changes: true))
            return;

        Dead();
        program.On(() =>
        {
            Assert.Contains("dead", Hired().Line);
            Assert.Equal("Level 86 Ascendant", Page.MercLevelLine);
            Assert.True(Page.ShowsMercRevive);

            Page.ReviveMercCommand.Execute(null);
            Assert.Equal("Mercenary revived.", Session.Status);
            Assert.False(Page.MercIsDead);
            Assert.False(Page.ShowsMercRevive);

            // Alive in the file, nothing else moved.
            Assert.Equal((86, 34, "Dark (Amplify Damage), Hell, level 86"), (Hired().Level, Hired().Type, Hired().Line));
        });
    }

    [CorpusFact]
    public void In_Legit_the_lines_stand_with_no_pencil_and_a_dead_one_has_its_badge_and_no_Revive()
    {
        if (!program.Ready(changes: true))
            return;

        Dead();
        try
        {
            Pad.In(program, "Legit", () => program.On(() =>
            {
                Assert.NotEmpty(Page.MercName);
                Assert.Equal("Level 86 Ascendant", Page.MercLevelLine);
                Assert.False(Page.EditsMerc);
                Assert.True(Page.MercIsDead);
                Assert.False(Page.ShowsMercRevive);

                // The pencil is not there to press; pressed all the same, nothing opens.
                Page.EditMercCommand.Execute(null);
                Assert.False(Page.IsMercEditOpen);
                Assert.False(Page.CanLowerMercLevel);
                Assert.False(Page.CanRaiseMercLevel);
            }));
        }
        finally
        {
            program.On(() => Page.IsMercEditOpen = false);
        }
    }

    /// <summary>
    /// The defect: a box handed a new value answers by writing one back - its list swapped, its
    /// text brought within its bounds - and while the page is reading the mercenary that is the page talking to
    /// itself, not the player (TakesMercEdit).
    /// </summary>
    [CorpusFact]
    public void What_a_box_writes_back_while_the_page_reads_the_mercenary_is_not_an_edit()
    {
        if (!program.Ready(changes: true))
            return;

        // The three boxes, each answering what it is told with a value of its own.
        int answered = 0;
        void Answer(object? sender, PropertyChangedEventArgs said)
        {
            if (said.PropertyName == nameof(Page.MercLevelText))
                Page.MercLevelText = "1";
            else if (said.PropertyName == nameof(Page.MercNameIndex))
                Page.MercNameIndex = Page.MercNameIndex == 0 ? 1 : 0;
            else if (said.PropertyName == nameof(Page.MercVariantIndex))
                Page.MercVariantIndex = Page.MercVariantIndex == 0 ? 1 : 0;
            else
                return;

            answered++;
        }

        Pad.In(program, "Free", () => program.On(() =>
        {
            var before = Hired();
            Page.PropertyChanged += Answer;
            try
            {
                // An edit reads the mercenary again, and the boxes are told.
                Page.LowerMercLevelCommand.Execute(null);
            }
            finally
            {
                Page.PropertyChanged -= Answer;
            }

            Assert.Equal(3, answered);
            Assert.Equal("Mercenary level 85.", Session.Status);
            Assert.Equal((85, before.Type, before.NameId), (Hired().Level, Hired().Type, Hired().NameId));

            // The same said once the page has read: the player's edit, and taken.
            Page.MercLevelText = "1";
            Assert.Equal(1, Hired().Level);
        }));
    }

    /// <summary>
    /// The same defect as it was met: a save the game had written - of a character with no mercenary - read
    /// again, the level's box handed "0" and bringing it to its least, 1; refused, and "No mercenary." took the
    /// place of "reloaded. Saved in game." on the strip. The box is the popup's own, bound as the page binds it.
    /// </summary>
    [CorpusFact]
    public void A_save_read_again_with_no_mercenary_leaves_the_strip_saying_it_was_reloaded()
    {
        if (!program.Ready(changes: true))
            return;

        string none = Path.Combine(Program.Corpus, "saves", "FigureTest.d2s");
        if (!File.Exists(none))
            return;

        NumericTextBox? box = null;
        try
        {
            program.On(() =>
            {
                box = new NumericTextBox { Minimum = 1, DataContext = Page };
                box.Bind(NumericTextBox.MaximumProperty, new Binding(nameof(Page.MercLevelMost)));
                box.Bind(TextBox.TextProperty, new Binding(nameof(Page.MercLevelText)) { Mode = BindingMode.TwoWay });
                Assert.Equal("86", box.Text);

                program.Get<AutoSave>().Flush();
                File.Copy(none, program.SaveOf(Program.Open), overwrite: true);
                Main.Sync!.Check();
            });

            Assert.True(program.Until(() => !Page.HasMerc), "the save with no mercenary was not read");
            Assert.True(program.Until(() => box!.Text == "1"), "the box did not bring what it was handed to its least");

            // The box's answer comes a moment after it is told: waited for, and never seen.
            Assert.False(program.Until(() => Session.Status != $"{Program.Open} reloaded. Saved in game.", 600),
                         "the strip said: " + program.On(() => Session.Status));
        }
        finally
        {
            program.On(() =>
            {
                if (box is not null)
                    box.DataContext = null;
            });
        }
    }

    private List<string?> Hear(out Action stop)
    {
        var heard = new List<string?>();
        void Heard(object? _, PropertyChangedEventArgs said) => heard.Add(said.PropertyName);
        Page.PropertyChanged += Heard;
        stop = () => Page.PropertyChanged -= Heard;
        return heard;
    }

    /// <summary>
    /// The page is kept while Settings changes the mode: the pencil, the level's steppers and their tooltips follow it
    /// then, each way, not at the next edit or move.
    /// </summary>
    [CorpusFact]
    public void A_mode_changed_tells_the_pencil_and_the_level_each_way()
    {
        if (!program.Ready())
            return;

        Pad.In(program, "Casual", () => program.On(() =>
        {
            var settings = program.Get<Services.Settings.EditorSettings>();
            var heard = Hear(out var stop);
            try
            {
                string[] told =
                [
                    nameof(Page.EditsMerc), nameof(Page.CanLowerMercLevel), nameof(Page.CanRaiseMercLevel), nameof(Page.MercLevelMost),
                    nameof(Page.MercLevelHint), nameof(Page.RaiseMercLevelHint), nameof(Page.ShowsMercRevive),
                ];

                settings.PlayMode = "Legit";
                Assert.All(told, name => Assert.Contains(name, heard));
                Assert.False(Page.EditsMerc);

                heard.Clear();
                settings.PlayMode = "Casual";
                Assert.All(told, name => Assert.Contains(name, heard));
                Assert.True(Page.EditsMerc);

                heard.Clear();
                settings.PlayMode = "Free";
                Assert.All(told, name => Assert.Contains(name, heard));
                Assert.Equal(MercenaryEdit.MostLevel, Page.MercLevelMost);
            }
            finally
            {
                stop();
            }
        }));
    }

    /// <summary>
    /// The popup is the page's, and the page is kept: open behind another page, it is still open when the mode comes to
    /// lock what it edits. It goes then, and what it pressed does nothing.
    /// </summary>
    [CorpusFact]
    public void A_mode_that_locks_the_mercenary_closes_its_popup_and_one_that_does_not_leaves_it()
    {
        if (!program.Ready())
            return;

        Pad.In(program, "Casual", () =>
        {
            program.On(() =>
            {
                var settings = program.Get<Services.Settings.EditorSettings>();
                Page.EditMercCommand.Execute(null);
                Assert.True(Page.IsMercEditOpen);

                settings.PlayMode = "Free";
                Assert.True(Page.IsMercEditOpen);

                settings.PlayMode = "Legit";
                Assert.False(Page.IsMercEditOpen);
            });
        });
    }

    /// <summary>In a mode that edits no character the level's commands and Revive change nothing, as its Ctrl and Shift steps did not already.</summary>
    [CorpusFact]
    public void In_Legit_the_level_commands_and_Revive_do_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        Dead();
        Pad.In(program, "Legit", () => program.On(() =>
        {
            var before = Hired();
            Page.LowerMercLevelCommand.Execute(null);
            Page.RaiseMercLevelCommand.Execute(null);
            Page.StepMercLevel(-5);
            Page.ReviveMercCommand.Execute(null);

            Assert.Equal(before, Hired());
            Assert.True(Page.MercIsDead);
        }));
    }

    /// <summary>
    /// Told of the mode, a box answers as it does when it reads the mercenary again - its level past the new range, written
    /// back - and that answer is the page talking to itself: a mercenary raised in Free keeps its level going to Casual.
    /// </summary>
    [CorpusFact]
    public void A_box_that_answers_a_mode_changed_does_not_edit_the_mercenary()
    {
        if (!program.Ready(changes: true))
            return;

        void Answer(object? sender, PropertyChangedEventArgs said)
        {
            if (said.PropertyName == nameof(Page.MercLevelText))
                Page.MercLevelText = Page.MercLevelMost.ToString();
        }

        Pad.In(program, "Free", () => program.On(() =>
        {
            Page.StepMercLevel(MercenaryEdit.MostLevel);
            Assert.Equal(MercenaryEdit.MostLevel, Session.Mercenary.Level);

            Page.PropertyChanged += Answer;
            try
            {
                program.Get<Services.Settings.EditorSettings>().PlayMode = "Casual";
            }
            finally
            {
                Page.PropertyChanged -= Answer;
            }

            Assert.Equal(MercenaryEdit.MostLevel, Session.Mercenary.Level);
        }));
    }
}
