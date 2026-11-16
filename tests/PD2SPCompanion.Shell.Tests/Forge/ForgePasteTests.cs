using System.Text.RegularExpressions;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The Forge's "Paste from game":
/// the item the game copies on Ctrl + C, made again in the holder. The button is greyed while the clipboard
/// holds anything else and says what it does; lit, it names the item. The clipboard is looked at when the page
/// is asked to look (the window come to the front) and when the Forge is shown - never on a timer. Pressed, it
/// makes what the clipboard holds now: a rare whose dropped line was put back, so the strip says to check; a
/// runeword with its runes; a magic map under its own name - and the four boxes say what each is.
/// <para>
/// What the page decides, on real copies (the corpus's clipboard texts) handed to it as its
/// clipboard. How a copied text is read into an item is the Core's (GameClipboardTests). That the window's own
/// clipboard reaches the page, and that the window come back to makes the page look, are the UI cases'.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgePasteTests(Program program)
{
    private MainViewModel Main => program.Main;

    /// <summary>One copied item of the corpus's, as the game put it on the clipboard: the one holding these words.</summary>
    private static string Text(string holding)
    {
        foreach (string file in new[] { "round1.txt", "round2.txt" })
        {
            string all = File.ReadAllText(Path.Combine(Program.Corpus, "clipboard", file));
            foreach (string one in Regex.Split(all, @"(?m)^\}\r?\n?").Where(each => each.Trim().Length > 0))
            {
                if (one.Contains(holding, StringComparison.Ordinal))
                    return one + "}";
            }
        }

        throw new InvalidOperationException($"The corpus's clipboard texts hold no item with {holding}.");
    }

    private static string Rare => Text("\"name\": \"Blood Call\"");

    private static string Word => Text("\"runeword\": \"Spirit\"");

    private static string Map => Text("\"type\": \"Demon Road Map\"");

    /// <summary>Copied, and the page asked to look, as the window come to the front asks it.</summary>
    private void Copy(ForgeBench bench, string text)
    {
        bench.Copied = text;
        program.On(() => _ = bench.Forge.Peek());
    }

    [CorpusFact]
    public void With_nothing_of_the_games_copied_the_button_is_greyed_and_says_what_it_does()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, "Harlequin Crest");

        program.On(() =>
        {
            Assert.False(bench.Forge.CanPaste);
            Assert.Null(bench.Forge.Copied);
            Assert.Equal("Paste from game", bench.Forge.PasteTip);
            Assert.False(bench.Forge.PasteCopiedCommand.CanExecute(null));
        });
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void An_item_copied_lights_nothing_until_the_page_looks_and_no_timer_looks()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, "Harlequin Crest");
        int looked = program.On(() => bench.Reads);

        // Copied while the window stays in front: nothing asks the page to look, and it does not look by itself.
        bench.Copied = Rare;
        Assert.False(bench.Until(() => bench.Forge.CanPaste, 1500), "the button lit with nothing having asked the page to look");
        Assert.Equal(looked, program.On(() => bench.Reads));

        program.On(() => _ = bench.Forge.Peek());
        program.On(() =>
        {
            Assert.True(bench.Forge.CanPaste);
            Assert.Equal("Paste Blood Call", bench.Forge.PasteTip);
            Assert.True(bench.Forge.PasteCopiedCommand.CanExecute(null));

            // A look makes nothing: the holder is as it was.
            Assert.False(bench.Forge.HasDraft);
        });
    }

    [CorpusFact]
    public void The_Forge_shown_again_looks_at_the_clipboard_again()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, Rare);

        // Another bookmark and back, another item copied meanwhile.
        program.On(() => Main.ShowRightCommand.Execute(MainViewModel.EditMark));
        bench.Copied = Word;
        program.On(() =>
        {
            Assert.Equal("Paste Blood Call", bench.Forge.PasteTip);
            Main.ShowRightCommand.Execute(MainViewModel.ForgeMark);
        });

        Assert.True(bench.Until(() => bench.Forge.PasteTip == "Paste Spirit"), "the Forge shown again did not look again");
    }

    [CorpusFact]
    public void A_rare_pasted_is_made_in_the_holder_the_boxes_say_what_it_is_and_the_strip_says_to_check_its_lines()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, Rare);
        program.On(() => bench.Forge.PasteCopiedCommand.Execute(null));

        Assert.True(bench.Until(() => bench.Forge.HasDraft), "nothing was pasted");
        program.On(() =>
        {
            Assert.Equal("Blood Call", bench.Forge.Draft!.Name);
            Assert.Same(bench.Forge.Draft, bench.Panel.Summary);
            Assert.Equal("Divine Scepter", bench.Forge.Base.Shown);
            Assert.Equal("Rare", bench.Forge.Rarity.Shown);
            Assert.Null(bench.Forge.Named);

            // A line the game's text dropped was put back: the strip says to check.
            Assert.Equal("Blood Call pasted. Check its lines: the game's text leaves some out.", bench.Session.RightMessages.Text);
            Assert.True(bench.Forge.ForgeCommand.CanExecute(null));

            // Nothing is pasted into the search box: it is for a name.
            Assert.Equal("", bench.Forge.Search);

            // Still on the clipboard: the button stays lit, and names it.
            Assert.Equal("Paste Blood Call", bench.Forge.PasteTip);
        });
    }

    [CorpusFact]
    public void A_runeword_pasted_comes_with_its_runes_in_it_and_nothing_to_check()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, Word);
        program.On(() =>
        {
            Assert.Equal("Paste Spirit", bench.Forge.PasteTip);
            bench.Forge.PasteCopiedCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Forge.HasDraft), "nothing was pasted");
        program.On(() =>
        {
            Assert.Equal("Spirit", bench.Forge.Draft!.Name);
            Assert.Equal("Crystal Sword", bench.Forge.Base.Shown);
            Assert.Equal("Runeword", bench.Forge.Rarity.Shown);
            Assert.Equal("Spirit", bench.Forge.Named?.Name);

            var runes = bench.Panel.Sockets.Rows.Select(socket => socket.Name).ToList();
            Assert.Contains("Tal Rune", runes);
            Assert.Contains("Amn Rune", runes);
            Assert.DoesNotContain(bench.Panel.Sockets.Rows, socket => socket.IsEmpty);

            // Nothing missing: the strip says only that.
            Assert.Equal("Spirit pasted.", bench.Session.RightMessages.Text);
        });
    }

    [CorpusFact]
    public void A_magic_map_pasted_is_named_as_it_was_and_stays_in_the_holder_when_the_clipboard_is_emptied()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, Map);
        program.On(() =>
        {
            Assert.Contains("Map", bench.Forge.PasteTip);
            bench.Forge.PasteCopiedCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Forge.HasDraft), "nothing was pasted");
        program.On(() =>
        {
            Assert.Equal("Shocking Demon Road Map of the Armorer", bench.Forge.Draft!.Name);
            Assert.Equal("Magic", bench.Forge.Rarity.Shown);
        });

        // Gone from the clipboard: greyed again once the page looks, and the holder keeps what was pasted.
        Copy(bench, "nothing of the game's");
        program.On(() =>
        {
            Assert.False(bench.Forge.CanPaste);
            Assert.Equal("Paste from game", bench.Forge.PasteTip);
            Assert.Equal("Shocking Demon Road Map of the Armorer", bench.Forge.Draft?.Name);
        });
    }

    [CorpusFact]
    public void The_button_pastes_what_the_clipboard_holds_now_not_what_it_held_when_it_was_looked_at()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        Copy(bench, Rare);

        // Another item copied since the look, with the button still naming the first.
        bench.Copied = Word;
        program.On(() =>
        {
            Assert.Equal("Paste Blood Call", bench.Forge.PasteTip);
            bench.Forge.PasteCopiedCommand.Execute(null);
        });

        Assert.True(bench.Until(() => bench.Forge.HasDraft), "nothing was pasted");
        program.On(() =>
        {
            Assert.Equal("Spirit", bench.Forge.Draft!.Name);
            Assert.Equal("Paste Spirit", bench.Forge.PasteTip);
        });

        // And gone from it since: said, the button greyed from here on, the holder left as it was.
        Copy(bench, Rare);
        bench.Copied = "nothing of the game's";
        program.On(() => bench.Forge.PasteCopiedCommand.Execute(null));

        Assert.True(bench.Until(() => !bench.Forge.CanPaste), "the button stayed lit over a clipboard holding no item");
        program.On(() =>
        {
            Assert.Equal("No item copied from the game.", bench.Session.RightMessages.Text);
            Assert.Equal("Spirit", bench.Forge.Draft?.Name);
        });
    }

    /// <summary>A unique this season's tables do not have, on a base of several: written, it would be row 0's.</summary>
    [CorpusFact]
    public void A_copied_unique_with_no_row_this_season_is_refused_and_named()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.False(bench.Forge.Paste("""{ "quality": "Unique", "name": "Bygone Wonder", "type": "Amulet", "iLevel": 50 }"""));
            Assert.Equal("Bygone Wonder not made: not in this season.", bench.Session.RightMessages.Text);
            Assert.False(bench.Forge.HasDraft);
        });
    }
}
