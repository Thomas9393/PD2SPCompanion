using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The Forge's roll: an easy reroll to simulate a
/// randomized item spawn - where the Edit page's is the game's Demonic Cube and rolls the stats alone.
/// Plain, it rolls every number the game rolls when it makes the item: the stats, the staff mods' levels, the
/// graphic, the base defense, the item's own socket range, its current durability. With Shift, what kind of item it is
/// too: ethereal or not, new affixes with a rare's name, the automod's
/// row, the staff mods, a superior item's bonus, a normal or superior base's sockets. Never the item level. And
/// whatever it rolls is an item the game could have made.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeRollTests(Program program)
{
    private static void Made(ForgeBench bench) => Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

    [CorpusFact]
    public void Plain_it_rolls_the_graphic_of_an_item_of_several_and_says_what_it_touches()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("stone of jor", "The Stone of Jordan"));
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.True(panel.RollsAsDrop);
            Assert.True(panel.ShowsOrb);
            Assert.False(panel.ShowsCube);
            Assert.True(panel.Basic.HasGraphic);

            // A ring: no defense, no sockets of its own, nothing ethereal, no affixes - so no line for Shift.
            Assert.Equal("Roll as a new drop (Ctrl + R)" + Environment.NewLine + "Rerolls stats and graphic", panel.RerollHint);

            // Each picture alike, the one worn among them (the roll draws as the game does; the die beside
            // the box never lands on the same): twelve presses do not all keep it.
            string was = panel.Basic.Graphic;
            bool drawn = false;
            for (int press = 0; press < 12 && !drawn; press++)
            {
                panel.RerollItemCommand.Execute(null);
                drawn = panel.Basic.Graphic != was;
            }

            Assert.True(drawn, "twelve rolls left the ring's picture as it was");
            Assert.StartsWith("The Stone of Jordan rolled: ", bench.Session.RightMessages.Text);

            // RESET is the item as chosen again.
            Assert.True(bench.Forge.ResetCommand.CanExecute(null));
        });
    }

    [CorpusFact]
    public void Plain_it_rolls_the_base_defense_and_the_items_own_socket_range()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);

        // Goldwrap spawns with Enhanced Defense, and the game then fixes its base defense: nothing of it to roll.
        program.On(() => bench.Find("goldwrap", "Goldwrap"));
        Made(bench);
        program.On(() => Assert.DoesNotContain("defense", bench.Panel.RerollHint));

        // A plain Quilted Armor: its base defense in a range, and nothing else of it to roll.
        program.On(() => bench.Find("quilted", "Quilted Armor"));
        Made(bench);
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.Equal(
                "Roll as a new drop (Ctrl + R)" + Environment.NewLine + "Rerolls defense and durability" + Environment.NewLine
                + "Shift + LMB: ethereal and sockets too",
                panel.RerollHint);

            var seen = new HashSet<string>();
            for (int press = 0; press < 40 && seen.Count < 2; press++)
            {
                panel.RerollItemCommand.Execute(null);
                seen.Add(panel.Basic.Defense);
                Assert.Empty(panel.Findings.Select(row => row.Said));
            }

            Assert.True(seen.Count > 1, "forty rolls left the armor's defense where it stood");

            // Plain never makes it ethereal, nor moves its level.
            Assert.False(panel.Basic.IsEthereal);
        });

        // Witchwild String: a socket range of its own, which the Sockets section's die names.
        program.On(() => bench.Find("witchwild", "Witchwild String"));
        Made(bench);
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.Contains("sockets", panel.RerollHint);

            var counts = new HashSet<string>();
            for (int press = 0; press < 40 && counts.Count < 2; press++)
            {
                panel.RerollItemCommand.Execute(null);
                counts.Add(panel.Sockets.Count);
            }

            Assert.True(counts.Count > 1, "forty rolls left the bow's sockets where they stood");

            // Within its own range, as the section's die says it: "Reroll within 2-4".
            var range = System.Text.RegularExpressions.Regex.Match(panel.Sockets.RollHint ?? "", @"within (\d+)-(\d+)");
            Assert.True(range.Success, $"the die says no range: {panel.Sockets.RollHint}");
            int least = int.Parse(range.Groups[1].Value), most = int.Parse(range.Groups[2].Value);
            Assert.All(counts, count => Assert.InRange(int.Parse(count), least, most));
        });
    }

    [CorpusFact]
    public void With_Shift_it_draws_new_affixes_and_a_name_and_ethereal_or_not_and_every_roll_is_an_item_the_game_makes()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war sword", "War Sword");
            bench.Rarity("Rare");
        });
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.Contains("Shift + LMB: ethereal and affixes too", panel.RerollHint);

            string level = panel.Basic.ItemLevel;
            string Affixes() => string.Join("|", panel.Stats.Select(row => row.Name).Order());
            var affixes = new HashSet<string> { Affixes() };
            var names = new HashSet<string> { bench.Forge.Draft!.Name };
            bool ethereal = false, plain = false;

            for (int roll = 0; roll < 300; roll++)
            {
                panel.RollAsNewDrop();
                affixes.Add(Affixes());
                names.Add(bench.Forge.Draft!.Name);
                ethereal |= panel.Basic.IsEthereal;
                plain |= !panel.Basic.IsEthereal;

                // An item the game could have made, each time; and at the level it was chosen at.
                Assert.Empty(panel.Findings.Select(row => row.Said));
                Assert.False(panel.HasChanges);
                Assert.Equal(level, panel.Basic.ItemLevel);
            }

            Assert.True(affixes.Count > 10, $"300 fuller rolls drew {affixes.Count} sets of affixes");
            Assert.True(names.Count > 10, $"300 fuller rolls drew {names.Count} names");

            // One in twenty: in 300 rolls both are met (all plain is 0.95^300, one in five million). At 150 it was
            // one in two thousand, and it came - a run of the whole check failed on nothing.
            Assert.True(ethereal, "300 fuller rolls made no ethereal sword");
            Assert.True(plain, "300 fuller rolls made only ethereal swords");
        });
    }

    /// <summary>
    /// Two defects of the Perfect Skull on a rare as the Forge makes it, which the fuller roll met about one time
    /// in six hundred. A skill tree's line was held one way by the item and another by the page's
    /// copy of it (ItemStats.SameLine), so the copy wore the tree twice - a finding on an item nobody had touched,
    /// and after a redraw a tree nothing accounted for. And the sockets a socket prefix bores stayed when a redraw
    /// took the prefix off: three on a rare, where the most anything bores is two. Both met here by making the
    /// sword again until it has the one or the other.
    /// </summary>
    [CorpusFact]
    public void A_rare_made_with_a_skill_tree_or_a_socket_prefix_is_sound_as_made_and_after_its_affixes_are_drawn_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war sword", "War Sword");
            bench.Rarity("Rare");
        });
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            int trees = 0, bored = 0;

            for (int made = 0; made < 600 && (trees < 3 || bored < 3); made++)
            {
                // RESET is the item made again, and dim until the item is other than as made.
                if (!bench.Forge.ResetCommand.CanExecute(null))
                    panel.RerollItemCommand.Execute(null);
                bench.Forge.ResetCommand.Execute(null);

                bool tree = bench.Held.StatLists[0].Stats.Any(stat => stat.Stat == "item_addskill_tab");

                // More than the Puzzlebox bores into a rare: a socket prefix's.
                bool prefix = bench.Held.TotalNumberOfSockets > 2;
                if (!tree && !prefix)
                    continue;

                trees += tree ? 1 : 0;
                bored += prefix ? 1 : 0;
                Assert.Empty(panel.Findings.Select(row => row.Said));

                for (int skull = 0; skull < 4; skull++)
                {
                    panel.Affixes.RerollAllAffixesNowCommand.Execute(null);
                    Assert.Empty(panel.Findings.Select(row => row.Said));
                    Assert.Equal(bench.Held.TotalNumberOfSockets.ToString(), panel.Sockets.Count);
                }
            }

            Assert.True(trees >= 3, $"600 swords made, {trees} with a skill tree");
            Assert.True(bored >= 3, $"600 swords made, {bored} with a socket prefix");
        });
    }

    [CorpusFact]
    public void The_fuller_roll_is_Shift_on_the_button_and_Ctrl_Shift_R()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war sword", "War Sword");
            bench.Rarity("Rare");
        });
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            string Affixes() => string.Join("|", panel.Stats.Select(row => row.Name).Order());

            // The plain roll keeps the affixes it has: their numbers alone move.
            string kept = Affixes();
            for (int press = 0; press < 5; press++)
                Assert.True(panel.RerollByKey());
            Assert.Equal(kept, Affixes());

            // The fuller one, by its key: another set, sooner or later.
            bool other = false;
            for (int press = 0; press < 10 && !other; press++)
            {
                Assert.True(panel.RerollByKey(full: true));
                other = Affixes() != kept;
            }

            Assert.True(other, "ten fuller rolls kept the same affixes");
        });
    }

    /// <summary>
    /// The page as it is drawn over the Forge's item: the Horadrim Orb stands where the Edit page has the Demonic
    /// Cube, a click on it is the plain roll, and a click with Shift held the fuller one.
    /// </summary>
    [CorpusFact]
    public void On_the_page_the_orb_stands_for_the_cube_and_Shift_with_a_click_is_the_fuller_roll()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war sword", "War Sword");
            bench.Rarity("Rare");
        });
        Made(bench);

        program.On(() =>
        {
            static void Pump()
            {
                for (int i = 0; i < 6; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                }
            }

            var panel = bench.Panel;
            var window = new Avalonia.Controls.Window { Width = 900, Height = 1000, Background = Brushes.Black, Content = new VP_View_Inv_R { DataContext = panel } };
            window.Show();
            Pump();
            try
            {
                var rolls = window.GetVisualDescendants().OfType<RollButton>().Where(button => button.IsEffectivelyVisible).ToList();
                var orb = Assert.Single(rolls, button => button.Kind == RollKind.Orb);
                Assert.DoesNotContain(rolls, button => button.Kind == RollKind.Cube);
                Assert.Equal("Item_RollAsDrop", AutomationProperties.GetAutomationId(orb));

                var at = orb.TranslatePoint(new Point(orb.Bounds.Width / 2, orb.Bounds.Height / 2), window)!.Value;
                void Click(RawInputModifiers held)
                {
                    window.MouseMove(at, held);
                    window.MouseDown(at, MouseButton.Left, held);
                    window.MouseUp(at, MouseButton.Left, held);
                    Pump();
                }

                string Affixes() => string.Join("|", panel.Stats.Select(row => row.Name).Order());
                string kept = Affixes();

                // A click: the plain roll, which says so and keeps the affixes.
                for (int press = 0; press < 5; press++)
                    Click(RawInputModifiers.None);
                Assert.StartsWith(bench.Forge.Draft!.Name + " rolled", bench.Session.RightMessages.Text);
                Assert.Equal(kept, Affixes());

                // With Shift held: another set of affixes, sooner or later.
                bool other = false;
                for (int press = 0; press < 10 && !other; press++)
                {
                    Click(RawInputModifiers.Shift);
                    other = Affixes() != kept;
                }

                Assert.True(other, "ten clicks with Shift kept the same affixes");
                Assert.Empty(panel.Findings.Select(row => row.Said));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>The range a die's tooltip names, "Reroll within 40-79".</summary>
    private static (int Least, int Most) Within(string hint)
    {
        var range = System.Text.RegularExpressions.Regex.Match(hint, @"within (\d+)-(\d+)");
        Assert.True(range.Success, $"the die says no range: {hint}");
        return (int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value));
    }

    /// <summary>
    /// Plain is every number: the staff mods' levels, their skills kept, and the current durability as a
    /// new item's - half the maximum to one under it, the durability die's range.
    /// </summary>
    [CorpusFact]
    public void Plain_it_rolls_the_staff_mods_levels_keeping_their_skills_and_the_current_durability()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("gothic staff", "Gothic Staff"));
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.True(panel.StaffSkills.Shows);

            // Staff mods first, by Shift - the game rolls none to three.
            for (int press = 0; press < 30 && !panel.StaffSkills.HasMods; press++)
                panel.RollAsNewDrop();
            Assert.True(panel.StaffSkills.HasMods, "thirty rolls with Shift drew no staff mod");
            Assert.Contains("staffmods", panel.RerollHint);
            Assert.Contains("durability", panel.RerollHint);

            var skills = panel.StaffSkills.HeldRows.Select(row => row.Skill).ToList();
            var (least, most) = Within(panel.Basic.DurabilityRerollHint);
            var lines = new HashSet<string>();
            var durabilities = new HashSet<string>();
            for (int press = 0; press < 40 && (lines.Count < 2 || durabilities.Count < 2); press++)
            {
                panel.RerollItemCommand.Execute(null);
                Assert.Equal(skills, panel.StaffSkills.HeldRows.Select(row => row.Skill).ToList());
                lines.Add(panel.StaffSkills.Now);
                durabilities.Add(panel.Basic.Durability);
                Assert.InRange(int.Parse(panel.Basic.Durability), least, most);
                Assert.Empty(panel.Findings.Select(row => row.Said));
            }

            Assert.True(lines.Count > 1, "forty rolls left the staff mods' levels where they stood");
            Assert.True(durabilities.Count > 1, "forty rolls left the durability where it stood");
        });
    }

    /// <summary>With Shift a superior item's bonus is drawn again; a normal item is never made superior.</summary>
    [CorpusFact]
    public void With_Shift_a_superior_items_bonus_is_drawn_again_and_a_normal_item_stays_normal()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("quilted", "Quilted Armor"));
        Made(bench);
        program.On(() =>
        {
            Assert.DoesNotContain("superior", bench.Panel.RerollHint);
            for (int press = 0; press < 20; press++)
            {
                bench.Panel.RollAsNewDrop();
                Assert.False(bench.Panel.Superior.CanDraw);
            }

            bench.Rarity("Superior");
        });
        Made(bench);

        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.True(panel.Superior.CanDraw);
            Assert.Contains("superior", panel.RerollHint);

            var bonuses = new HashSet<string>();
            for (int press = 0; press < 40 && bonuses.Count < 2; press++)
            {
                panel.RollAsNewDrop();
                bonuses.Add(panel.Superior.Now);
                Assert.True(panel.Superior.CanDraw);
                Assert.Empty(panel.Findings.Select(row => row.Said));
            }

            Assert.True(bonuses.Count > 1, "forty rolls with Shift left the superior bonus as it was");
        });
    }

    /// <summary>
    /// With Shift a normal base drops socketed one time in three, with one to the most it takes; a runeword's count is
    /// its runes' and never rolled.
    /// </summary>
    [CorpusFact]
    public void With_Shift_a_normal_base_drops_socketed_or_not_and_a_runewords_count_is_left()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("quilted", "Quilted Armor"));
        Made(bench);
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.True(panel.Sockets.CanDropRoll);
            var counts = new HashSet<int>();
            for (int press = 0; press < 60 && (counts.Count < 2 || !counts.Contains(0)); press++)
            {
                panel.RollAsNewDrop();
                counts.Add(int.Parse(panel.Sockets.Count));
            }

            Assert.Contains(0, counts);
            Assert.True(counts.Count > 1, "sixty rolls with Shift never dropped the armor socketed");
            Assert.All(counts, count => Assert.InRange(count, 0, panel.Sockets.Maximum));
        });

        program.On(() => bench.Find("insight", "Insight"));
        Made(bench);
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.False(panel.Sockets.CanDropRoll);
            string sockets = panel.Sockets.Count;
            for (int press = 0; press < 20; press++)
            {
                panel.RollAsNewDrop();
                Assert.Equal(sockets, panel.Sockets.Count);
            }
        });
    }

    /// <summary>With Shift the automod's row is drawn again, where the base's group has several.</summary>
    [CorpusFact]
    public void With_Shift_the_automods_row_is_drawn_again()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);

        // A class item of a group of several rows (the line shows only there).
        string? found = null;
        foreach (var (typed, name) in new[] { ("wolf head", "Wolf Head"), ("jawbone cap", "Jawbone Cap"), ("eagle orb", "Eagle Orb"), ("stag bow", "Stag Bow") })
        {
            program.On(() => bench.Find(typed, name));
            Made(bench);
            if (program.On(() => bench.Panel.Automagic.Shows))
            {
                found = name;
                break;
            }
        }

        Assert.True(found is not null, "no base with an automod of several rows among those tried");
        program.On(() =>
        {
            var panel = bench.Panel;
            Assert.Contains("automod", panel.RerollHint);
            var rows = new HashSet<string>();
            for (int press = 0; press < 40 && rows.Count < 2; press++)
            {
                panel.RollAsNewDrop();
                rows.Add(panel.Automagic.Now);
                Assert.Empty(panel.Findings.Select(row => row.Said));
            }

            Assert.True(rows.Count > 1, $"forty rolls with Shift left {found}'s automod as it was");
        });
    }
}
