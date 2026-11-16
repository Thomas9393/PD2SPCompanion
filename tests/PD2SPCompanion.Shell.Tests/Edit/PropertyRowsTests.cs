using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The property rows of the Edit page. Stats no property writes apart are one number
/// and go on and off together: Enhanced Damage's maximum and minimum take each other's number, typed or
/// stepped, however many sources feed them; the bin on one takes both off and the undo brings both back; a
/// flat damage picked on a helm adds its three copies. The Demonic Cube's reroll reaches a line that names a
/// skill. And a number being typed: under its range it stays as typed and the item keeps the last number that
/// held, so a range from 12 can be typed into; past the range it is held at once.
/// <para>
/// What the rows and the page decide, asked as the view asks (a row's text, its commands, the picker's pick)
/// and read back from the item. What the box itself does - a number left unfinished held to the range when the
/// box is left or Enter is pressed, the other box of a pair showing the number typed into the first - is the
/// UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PropertyRowsTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    /// <summary>
    /// Ormus' Robes: the die on either of its two skill lines draws both, the cast's chance and
    /// level with them, as the cube does.
    /// </summary>
    [CorpusFact]
    public void The_die_on_ormus_robes_draws_both_skill_lines_together()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var robes = program.Give("ormus", "Ormus' Robes");
            Assert.NotNull(robes);
            program.Main.Session.SelectedItem = program.TileOf(robes!);

            StatRowViewModel Grant() => Edit.Stats.Single(row => row.Stat.Stat == "item_singleskill");
            StatRowViewModel Cast() => Edit.Stats.Single(row => row.Stat.Stat == "item_skilloncast");
            Assert.True(Grant().SkillIsDrawn);
            Assert.True(Cast().SkillIsDrawn);

            var source = AffixRanges.RandomSkillCastsOn(robes!).Single();
            foreach (var row in new[] { Grant, Cast, Grant, Cast })
            {
                int? was = Grant().Skill?.Id;
                row().RerollSkillCommand.Execute(null);

                int skill = Grant().Skill!.Id;
                Assert.NotEqual(was, skill);
                Assert.Equal(skill, Cast().Skill?.Id);
                Assert.Equal(RandomSkillCast.Chance(skill, source.Min).ToString(), Cast().Value);
                Assert.Equal(RandomSkillCast.Level(skill, source.Max), Cast().Stat.SkillLevel);
            }
        });
    }

    private const string Most = "item_maxdamage_percent", Least = "item_mindamage_percent";

    private (string, string) Pair => (Edit.Row(Most).Value, Edit.Row(Least).Value);

    /// <summary>
    /// Individual editing on all occasions, with a button to propagate the current box's value to the other synced
    /// boxes. Each of Enhanced Damage's two rows is typed into on its own, and the row's link button puts its
    /// number on the other.
    /// </summary>
    [CorpusFact]
    public void Enhanced_Damages_rows_are_typed_into_alone_and_the_link_puts_one_number_on_both()
    {
        if (!program.Ready(changes: true))
            return;

        Item limb = null!;
        program.On(() =>
        {
            program.Panes("Personal Stash");
            limb = program.Select("Demon Limb", "Personal Stash");
            Assert.Equal(("199", "199"), Pair);
            Assert.True(Edit.Row(Most).HasFellows && Edit.Row(Least).HasFellows);

            Edit.Row(Most).Value = "205";
            Assert.Equal(("205", "199"), Pair);

            Edit.Row(Least).Value = "181";
            Assert.Equal(("205", "181"), Pair);

            Edit.Row(Least).AskFellowsCommand.Execute(null);
            Assert.Equal(("181", "181"), Pair);
        });

        Assert.True(program.Until(() => limb.StatOf(Most) == 181 && limb.StatOf(Least) == 181), "the pair was not written");
    }

    /// <summary>
    /// Fed by two sources, an affix's and a corruption's: a step moves its own row, and the link the other - the
    /// halves an affix or corruption taken off had left apart are set alike again by it, or kept apart.
    /// </summary>
    [CorpusFact]
    public void Fed_by_two_sources_a_row_steps_alone_and_its_link_puts_the_number_on_the_other()
    {
        if (!program.Ready(changes: true))
            return;

        Item barb = null!;
        program.On(() =>
        {
            barb = program.Select("Stone Barb");
            Assert.Equal(("175", "175"), Pair);   // Season 14's: Visceral is no affix of a crafted spear there (pd2spc padseason)
            Assert.True(Edit.Row(Most).Roll is { Sources: > 1 });

            Edit.Row(Most).RaiseCommand.Execute(null);
            Assert.Equal(("176", "175"), Pair);

            Edit.Row(Most).AskFellowsCommand.Execute(null);
            Assert.Equal(("176", "176"), Pair);
        });

        Assert.True(program.Until(() => barb.StatOf(Most) == 176 && barb.StatOf(Least) == 176), "the pair was not written");
    }

    [CorpusFact]
    public void The_Demonic_Cubes_reroll_reaches_the_lines_that_name_a_skill()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Halaberd's Reign", "Personal Stash");
            List<string> Skills() => [.. Edit.Stats.Select(row => row.InGame).Where(line => line.Contains("Battle"))];

            var were = Skills();
            Assert.Contains("+2 to Battle Cry (Barbarian Only)", were);
            Assert.Contains("+2 to Battle Orders (Barbarian Only)", were);

            // Each is 2-3 in Season 14 (1-2 in 13), so a roll leaves both alone one time in four.
            bool moved = false;
            for (int roll = 0; roll < 16 && !moved; roll++)
            {
                Edit.RerollItemCommand.Execute(null);
                moved = !Skills().SequenceEqual(were);
            }

            Assert.True(moved, "sixteen rerolls left both skills' levels as they were");
            Assert.All(Skills(), line => Assert.Matches(@"^\+[23] to Battle (Cry|Orders) \(Barbarian Only\)$", line));
        });
    }

    /// <summary>
    /// A corruption may apply just one resistance on top of the all
    /// resistance, common and legal in game, like 20 all res => 50 fire res/20 cold res/20 light
    /// res/20 poison res. Kira's Guardian's All Resistances with a corruption's Fire Resist on top: the fire
    /// row is fed by two sources and holds their sum. No syncing is forced, but
    /// an extra control syncs the current value to the related boxes. A step moves its
    /// own row alone, whichever it is; asked (Alt on its stepper, Alt + Enter in its box), the rows All
    /// Resistances wrote take its number.
    /// </summary>
    [CorpusFact]
    public void A_resistance_moves_alone_and_its_group_takes_its_number_when_asked()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Main.Session.EnsureGame();
            var helm = ItemForge.Unique("Kira's Guardian", "103", RollAs.Minimum).Item;
            var fire = Corruptions.Available(helm).First(option => option.Grants.Any(grant => grant.Code == "res-fire"));
            Corruptions.Apply(helm, fire, new Random(7));
            program.Main.Session.SelectedItem = new ItemTileViewModel(helm, 0);
            try
            {
                StatRowViewModel Row(string stat) => Edit.Row(stat);
                (int Fire, int Cold, int Light, int Poison) Now() =>
                    (int.Parse(Row("fireresist").Value), int.Parse(Row("coldresist").Value), int.Parse(Row("lightresist").Value), int.Parse(Row("poisonresist").Value));

                var was = Now();
                Assert.Equal((was.Cold, was.Cold), (was.Light, was.Poison));
                Assert.True(was.Fire > was.Cold, "the corruption's resistance is not on top of All Resistances");

                // Two sources feed the fire row, and its range is the two ranges together; one feeds the others.
                Assert.Equal(2, Row("fireresist").Roll!.Sources);
                Assert.Equal(1, Row("coldresist").Roll!.Sources);
                Assert.Equal(Row("coldresist").Roll!.Min + fire.Grants.Single(grant => grant.Code == "res-fire").Min, Row("fireresist").Roll!.Min);
                Assert.Empty(Edit.Findings());

                // Each row has its link: all four were rolled by one property.
                Assert.All(new[] { "fireresist", "coldresist", "lightresist", "poisonresist" },
                    stat => Assert.True(Row(stat).HasFellows));

                // A step moves its own row, whichever it is.
                Row("lightresist").RaiseCommand.Execute(null);
                Assert.Equal((was.Fire, was.Cold, was.Light + 1, was.Poison), Now());
                Row("fireresist").RaiseCommand.Execute(null);
                Assert.Equal((was.Fire + 1, was.Cold, was.Light + 1, was.Poison), Now());

                // Asked from lightning: the other three take its number, fire too - in Free, where a row is held
                // to its field and not to its sources' range (in Casual fire stops at what its two sources give).
                Row("lightresist").AskFellowsCommand.Execute(null);
                Assert.Equal((was.Light + 1, was.Light + 1, was.Light + 1, was.Light + 1), Now());

                // And the item says each resistance on its own line - once the page has had its turn: the item's
                // lines are drawn again a moment after a number is kept.
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(Edit.Lines().Any(line => line.StartsWith($"All Resistances +{was.Light + 1}", StringComparison.Ordinal)),
                    string.Join(" | ", Edit.Lines()));
            }
            finally
            {
                program.Deselect();
            }
        });
    }

    /// <summary>
    /// The Demonic Cube draws the picture again where it is one of the base's several - seen in game on Gheed's
    /// Fortune and a unique ring, reversing the earlier
    /// "leaves the graphic", never seen in game. In Free it takes a magic ring; the Forge's fuller roll is no key here.
    /// </summary>
    [CorpusFact]
    public void On_an_item_that_is_someones_the_Demonic_Cube_rolls_the_numbers_and_draws_the_picture_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Steel Ring of Skill", "Personal Stash");
            Assert.True(Edit.Basic.HasGraphic);
            Assert.False(Edit.RollsAsDrop);
            Assert.Equal(Edit.CanReroll, Edit.ShowsCube);
            Assert.False(Edit.ShowsOrb);
            Assert.Equal("Reroll all stats (Ctrl + R)", Edit.RerollHint);

            // Each picture alike, the one worn among them: twelve presses do not all keep it.
            string was = Edit.Basic.Graphic;
            bool drawn = false;
            for (int press = 0; press < 12 && !drawn; press++)
            {
                Edit.RerollItemCommand.Execute(null);
                drawn = Edit.Basic.Graphic != was;
            }

            Assert.True(drawn, "twelve rolls of the cube left the ring's picture as it was");
            Assert.False(Edit.RerollByKey(full: true));
        });
    }

    /// <summary>
    /// Outside Free the cube takes what the game's takes: a unique or a set item - not a magic
    /// ring - and not a corrupted one, where it stands dimmed and says so. In Free it rolls both.
    /// </summary>
    [CorpusFact]
    public void In_Casual_the_cube_takes_uniques_and_sets_and_is_dimmed_on_a_corrupted_one_and_Free_takes_them_all()
    {
        if (!program.Ready(changes: true))
            return;

        using (program.InMode("Casual"))
        {
            program.On(() =>
            {
                program.Panes("Personal Stash");
                program.Select("Steel Ring of Skill", "Personal Stash");
                Assert.True(Edit.CanReroll);
                Assert.False(Edit.ShowsCube);
                Assert.False(Edit.RerollByKey());

                program.Select("Witchwild String", "Personal Stash");
                Assert.True(Edit.ShowsCube && Edit.CanRollNow);

                program.Panes("Inventory");
                program.Select("Veil of Steel", "Inventory");
                Assert.True(Edit.ShowsCube);
                Assert.False(Edit.CanRollNow);
                Assert.Equal("Reroll all stats (Ctrl + R)" + Environment.NewLine + "Corrupted.", Edit.RerollHint);
                string Numbers() => string.Join(",", Edit.Stats.Select(row => row.Value));
                string was = Numbers();
                Assert.False(Edit.RerollByKey());
                Edit.RerollItemCommand.Execute(null);
                Assert.Equal(was, Numbers());
                program.Deselect();
            });
        }

        using (program.InMode("Free"))
        {
            program.On(() =>
            {
                program.Panes("Inventory");
                program.Select("Veil of Steel", "Inventory");
                Assert.True(Edit.ShowsCube && Edit.CanRollNow);
                Assert.Equal("Reroll all stats (Ctrl + R)", Edit.RerollHint);
                program.Panes("Personal Stash");
                program.Select("Steel Ring of Skill", "Personal Stash");
                Assert.True(Edit.ShowsCube);
                program.Deselect();
            });
        }
    }

    /// <summary>
    /// Hellfire Torch's class: the Demonic Cube's reroll should randomize it, as the game engine is thought to.
    /// The class is drawn with the property, so a
    /// reroll of the item's properties draws it again; the row's own die does it alone.
    /// </summary>
    [CorpusFact]
    public void The_Demonic_Cubes_reroll_draws_Hellfire_Torchs_class_again_and_the_rows_die_does_it_alone()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            program.Select("Hellfire Torch", "Inventory");
            StatRowViewModel Row() => Edit.Stats.Single(row => row.ClassIsDrawn);
            string Class() => Row().ChosenIndex!.Name;

            Assert.True(Edit.CanReroll);
            string was = Class();
            Assert.Matches(@" Skill Levels", Row().InGame);

            // The cube: another class, every time - the draw leaves out the one held.
            Edit.RerollItemCommand.Execute(null);
            string cubed = Class();
            Assert.NotEqual(was, cubed);
            Assert.Contains(cubed, Row().InGame);

            // The row's die: the class alone, the numbers left.
            var numbers = Edit.Stats.Select(row => row.Value).ToList();
            Row().RerollClassCommand.Execute(null);
            Assert.NotEqual(cubed, Class());
            Assert.Equal(numbers, Edit.Stats.Select(row => row.Value));
        });
    }

    [CorpusFact]
    public void The_brush_on_one_of_Enhanced_Damages_rows_takes_both_off_and_the_undo_brings_both_back()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        Item veil = null!;
        program.On(() =>
        {
            veil = program.Select("Veil of Steel");
            Assert.Same(veil, program.Worn(ItemLocation.Head).Item!.Item);
            Assert.Equal("Properties  (12)", Edit.StatsHeader);

            var row = Edit.Row(Most);
            Assert.True(row.CanDelete);
            Assert.Equal("Remove property" + Environment.NewLine + "Adjacent run entries go too", row.DeleteHint);
            row.DeleteCommand.Execute(null);
        });

        Assert.True(program.Until(() => Edit.StatsHeader == "Properties  (10)"), program.On(() => Edit.StatsHeader));
        program.On(() =>
        {
            Assert.Null(veil.StatOf(Most));
            Assert.Null(veil.StatOf(Least));

            // The undo, once the change is a step of the history: written.
            program.Get<AutoSave>().Flush();
            Assert.StartsWith("Undo Edited Veil of Steel.", program.Main.Home.UndoHint);
            program.Main.Home.UndoCommand.Execute(null);
        });

        Assert.True(program.Until(() => program.Main.Session.SelectedItem?.Name == "Veil of Steel" && Edit.StatsHeader == "Properties  (12)"),
            program.On(() => $"{program.Main.Session.SelectedItem?.Name}: {Edit.StatsHeader}"));
        program.On(() =>
        {
            var back = program.Worn(ItemLocation.Head).Item!.Item;
            Assert.NotNull(back.StatOf(Most));
            Assert.NotNull(back.StatOf(Least));
        });
    }

    [CorpusFact]
    public void A_flat_damage_picked_on_a_helm_adds_its_three_copies()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Select("Veil of Steel");
            Assert.Equal("Properties  (12)", Edit.StatsHeader);
            Assert.True(Edit.CanAddStat);

            Edit.OpenStatPickerCommand.Execute(null);
            Assert.True(Edit.StatPicker.IsOpen);
            Edit.StatPicker.Search = "maximum throw";
            var line = Edit.StatPicker.Choices.First(choice => choice.Text == "+X to Maximum Throw Damage");

            // A pick is added and the picker closes: nothing is provisional.
            Edit.StatPicker.PickCommand.Execute(line);
            Assert.False(Edit.StatPicker.IsOpen);
        });

        Assert.True(program.Until(() => Edit.StatsHeader == "Properties  (15)" && !Edit.HasChanges), program.On(() => Edit.StatsHeader));
    }

    [CorpusFact]
    public void A_number_under_its_range_stays_as_typed_and_the_item_keeps_the_last_that_held()
    {
        if (!program.Ready(changes: true))
            return;

        // A number is held to its affix's range in every mode but Free.
        using var casual = program.InMode("Casual");
        Item ring = null!;
        StatRowViewModel life = null!;
        string stat = "";
        program.On(() =>
        {
            program.Panes("Personal Stash");
            ring = program.Select("Felicitous Ring of Regrowth", "Personal Stash");

            // Its Replenish Life: 15, of 12-18.
            life = Edit.Stats.First(row => row.Roll is { Min: 12, Max: 18 });
            stat = life.Name;
            Assert.Equal("15", life.Value);
            Assert.Equal((12, 18), (life.Minimum, life.Maximum));

            // The first digit of 17 is under the range: not put back to 12 before the 7 can follow.
            life.Value = "1";
            Assert.Equal("1", life.Value);
            Assert.Equal(15, life.Number);
            Assert.True(life.IsValid);
        });

        // Nothing is written of a number still being typed.
        program.Turned();
        program.On(() =>
        {
            Assert.Equal(15, ring.StatOf(stat));
            life = Edit.Stats.First(row => row.Name == stat);
            Assert.Equal("1", life.Value);
            life.Value = "17";
        });
        Assert.True(program.Until(() => ring.StatOf(stat) == 17), "17 was not written");

        program.On(() =>
        {
            // A digit taken off leaves the rest, and the item at 17.
            life = Edit.Stats.First(row => row.Name == stat);
            life.Value = "1";
            Assert.Equal("1", life.Value);
            Assert.Equal(17, life.Number);
        });
        program.Turned();
        program.On(() => Assert.Equal(17, ring.StatOf(stat)));
    }

    [CorpusFact]
    public void A_number_past_its_range_is_held_at_once()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        Item ring = null!;
        string stat = "";
        program.On(() =>
        {
            program.Panes("Personal Stash");
            ring = program.Select("Felicitous Ring of Regrowth", "Personal Stash");
            var life = Edit.Stats.First(row => row.Roll is { Min: 12, Max: 18 });
            stat = life.Name;

            life.Value = "99";
            Assert.Equal("18", life.Value);
        });

        Assert.True(program.Until(() => ring.StatOf(stat) == 18), "18 was not written");
    }

    /// <summary>
    /// A Faith in a superior base rolled to 600+ ED: double stacking, the superior part rolling the range as the
    /// runeword's ED does. The cube's reroll on the page: the base's Enhanced Damage row and the
    /// word's each take a number from their own list's sources.
    /// </summary>
    [CorpusFact]
    public void The_cube_rolls_a_superior_runewords_own_and_word_rows_each_in_their_own_range()
    {
        if (!program.Ready(changes: true))
            return;

        using var casual = program.InMode("Casual");
        program.On(() =>
        {
            program.Main.Session.EnsureGame();
            var word = Runewords.All.First(each => Runewords.GameName(each) == "Faith");
            var shown = ItemForge.RunewordShown(word, "103").Item;
            var faith = ItemForge.PlainBase(shown.Code.Trim(), word.Runes.Length, "103").Item;
            var words = new ItemStatList();
            words.Stats.AddRange(shown.StatLists[0].Stats.Select(stat => new ItemStat
            {
                Id = stat.Id, Stat = stat.Stat, Value = stat.Value, Param = stat.Param,
                SkillId = stat.SkillId, SkillLevel = stat.SkillLevel, MaxCharges = stat.MaxCharges,
            }));
            ItemForge.ApplyRuneword(faith, word, words);
            faith.Quality = ItemQuality.Superior;
            QualityItems.Put(faith, QualityItems.RowsFor(faith).First(row => row.Mods.Any(mod => mod.Code == "dmg%")));

            program.Main.Session.SelectedItem = new ItemTileViewModel(faith, 0);
            try
            {
                var own = Edit.Stats.Single(row => row.Stat.Stat == Most && !row.IsRuneword);
                var added = Edit.Stats.Single(row => row.Stat.Stat == Most && row.IsRuneword);
                var ownRange = AffixRanges.OwnOf(faith)[Most];
                var wordRange = AffixRanges.AddedTo(faith)[Most];

                for (int press = 0; press < 30; press++)
                {
                    Edit.RerollItemCommand.Execute(null);
                    Assert.InRange(int.Parse(own.Value), ownRange.Min, ownRange.Max);
                    Assert.InRange(int.Parse(added.Value), wordRange.Min, wordRange.Max);
                }
            }
            finally
            {
                program.Deselect();
            }
        });
    }
}
