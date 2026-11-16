namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// A monster parameter's panel, which was very slow: its 1,240 choices were made anew at
/// every read - the box, its label, the panel's open, each keystroke. Made once now and cut down only when what is
/// typed changes; the row says the monster chosen even while the search hides it. A Sacred Totem's "Reanimate as".
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MonsterPanelTests(Program program)
{
    [CorpusFact]
    public void The_monster_list_is_made_once_and_cut_down_only_by_what_is_typed()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() => bench.Find("sacred totem", "Sacred Totem"));
        Assert.True(bench.Until(() => bench.Panel.Stats.Any(row => row.Name == "item_reanimate")), "the totem has no Reanimate row");

        program.On(() =>
        {
            var row = bench.Panel.Stats.First(each => each.Name == "item_reanimate" && each.IndexDisplay.StartsWith("19 - "));
            Assert.True(row.IndexInPanel);
            string chosen = row.IndexDisplay;
            Assert.StartsWith("19 - ", chosen);   // Fallen, the first of its two lines

            var whole = row.IndexChoices;
            Assert.True(whole.Count > 1000);
            Assert.Same(whole, row.IndexChoices);

            // Reads cost nothing now: a thousand of them, well under what one build of the list took.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int read = 0; read < 1000; read++)
                _ = (row.IndexChoices, row.IndexDisplay, row.ChosenIndex, row.IndexNeedsPanel, row.NothingMatches);
            Assert.True(clock.ElapsedMilliseconds < 200, $"1000 reads took {clock.ElapsedMilliseconds} ms");

            // Typed: cut down, the same list until the text changes - and the row still says the one chosen.
            row.SkillSearch = "fallen";
            var cut = row.IndexChoices;
            Assert.InRange(cut.Count, 1, 60);
            Assert.All(cut, choice => Assert.Contains("fallen", choice.Name, StringComparison.OrdinalIgnoreCase));
            Assert.Same(cut, row.IndexChoices);
            Assert.Equal(chosen, row.IndexDisplay);
            Assert.True(row.IndexInPanel);

            // Nothing matches: said, the panel still the panel.
            row.SkillSearch = "no such monster";
            Assert.True(row.NothingMatches);

            row.SkillSearch = "";
            Assert.Same(whole, row.IndexChoices);
        });
    }
}
