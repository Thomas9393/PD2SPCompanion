using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// The right-click menu's own parts and the clipboard's, with no program behind them: that every glyph
/// a menu can name is a glyph there is - a name mistyped draws a row with no icon and says nothing; what the
/// menu's service does with an empty menu, a dimmed act and the icons' rules; a row's count and its lesser form;
/// and what the clipboard holds, says and lets go of.
/// <para>
/// With the program's tests, one at a time, and the two that touch the window's one menu and one clipboard on its
/// thread: the pages listen to both, and tell their views.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ActMenuTests(Program program)
{
    /// <summary>The repository's top, found from where the tests run: the folder that holds the solution.</summary>
    private static string Root()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "PD2SPCompanion.slnx")))
                return at.FullName;
        }

        throw new InvalidOperationException("The solution's folder was not found above the tests.");
    }

    [CorpusFact]
    public void Every_glyph_a_menu_can_name_is_a_glyph_there_is()
    {
        string glyphs = File.ReadAllText(Path.Combine(Root(), "src", "PD2SPCompanion.Shell", "Resources", "UI_Main", "Glyphs.axaml"));

        foreach (var glyph in Enum.GetValues<MenuGlyph>().Where(each => each != MenuGlyph.None))
            Assert.True(glyphs.Contains($"x:Key=\"{MenuAct.KeyOf(glyph)}\"", StringComparison.Ordinal), $"no {MenuAct.KeyOf(glyph)} in Glyphs.axaml");

        Assert.Equal("[Glyph] Open folder", MenuAct.KeyOf(MenuGlyph.OpenFolder));
        Assert.Equal("[Glyph] Bin", MenuAct.KeyOf(MenuGlyph.Bin));
    }

    [CorpusFact]
    public void The_service_shows_no_empty_menu_marks_the_icons_after_the_first_and_runs_no_dimmed_act()
    {
        if (program.Ready())
            program.On(ServiceShowsAndPicks);
    }

    private static void ServiceShowsAndPicks()
    {
        var service = ActMenuService.Shared;
        int asked = 0, shut = 0, ran = 0;
        void Asked(object? beside, Avalonia.Rect? at) => asked++;
        void Shut() => shut++;
        service.Asked += Asked;
        service.Shut += Shut;

        try
        {
            // Nothing can be done to what was clicked: nothing is shown, and a menu that was up is closed.
            service.Show(new ActMenu([], []));
            Assert.Equal((0, 1), (asked, shut));

            var cut = new MenuAct("Cut", MenuGlyph.Cut, () => ran++);
            var paste = new MenuAct("Paste", MenuGlyph.Paste, () => ran++) { IsEnabled = false, Why = "Nothing held" };
            var open = new MenuAct("Open", MenuGlyph.Open, () => ran++) { IsMain = true };
            service.Show(new ActMenu([cut, paste], [open]));
            Assert.Equal(1, asked);
            Assert.True(service.HasIcons);
            Assert.Same(open, service.Rows[0]);

            // A line between two icons, none before the first.
            Assert.False(cut.FollowsAnother);
            Assert.True(paste.FollowsAnother);

            // A dimmed act closes the menu and does nothing; a live one runs - at once here, later in the window.
            service.Pick(paste);
            Assert.Equal(0, ran);
            service.Pick(open);
            Assert.Equal(1, ran);
            Action? held = null;
            service.Pick(cut, run => held = run);
            Assert.Equal(1, ran);
            held!();
            Assert.Equal(2, ran);
        }
        finally
        {
            service.Asked -= Asked;
            service.Shut -= Shut;
        }
    }

    [CorpusFact]
    public void A_row_counts_what_it_takes_says_why_it_is_dimmed_and_has_a_lesser_form()
    {
        Assert.Equal("Store in vault", ActMenu.Counted("Store in vault", 1));
        Assert.Equal("Store in vault (3)", ActMenu.Counted("Store in vault", 3));

        var equip = new MenuAct("Equip", MenuGlyph.BattleGear, () => { }) { IsMain = true, IsEnabled = false, Why = "Requires level 60", Keys = "Enter" };
        Assert.Equal("Requires level 60", equip.RowTip);
        Assert.Equal("Equip (Enter)" + Environment.NewLine + "Requires level 60", equip.IconTip);
        Assert.True(equip.IsArt);
        Assert.Equal(Avalonia.Media.FontWeight.Bold, equip.Weight);

        // Under another row that is the menu's first: the same act, not bold, a rule above it where asked.
        var lesser = equip.Lesser(ruleAbove: true);
        Assert.False(lesser.IsMain);
        Assert.True(lesser.RuleAbove);
        Assert.Equal(("Equip", "Enter", false, "Requires level 60"), (lesser.Label, lesser.Keys, lesser.IsEnabled, lesser.Why));

        Assert.Null(new MenuAct("Open", MenuGlyph.Open, () => { }).RowTip);
        Assert.False(new MenuAct("Sort by", MenuGlyph.Sort, () => { }).HasMore);
        Assert.True(new MenuAct("Sort by", MenuGlyph.Sort, () => { }) { More = [new MenuAct("Name", MenuGlyph.None, () => { })] }.HasMore);
    }

    [CorpusFact]
    public void The_clipboard_holds_one_kind_of_thing_says_what_and_lets_the_characters_go_alone()
    {
        if (program.Ready())
            program.On(ClipboardHolds);
    }

    private static void ClipboardHolds()
    {
        var clip = ItemClipboard.Shared;
        int changed = 0;
        void Changed() => changed++;
        clip.Clear();
        clip.Changed += Changed;

        try
        {
            Assert.True(clip.IsEmpty);
            Assert.Equal("", clip.Says);

            // Letting go of nothing tells nobody.
            clip.Clear();
            Assert.Equal(0, changed);

            Item one = new(), two = new();
            clip.HoldItems(ClipKind.Cut, [one, two]);
            Assert.Equal((ClipKind.Cut, 2, "2 items cut"), (clip.Kind, clip.Count, clip.Says));
            Assert.Equal(1, changed);

            // What is held next takes the place of what was.
            clip.HoldCards(ClipKind.Copy, [7L]);
            Assert.Equal((ClipKind.Copy, 1, "1 item copied"), (clip.Kind, clip.Count, clip.Says));
            Assert.Empty(clip.Items);

            // The character closed: its items go, the vault's stay.
            Assert.False(clip.DropItems());
            Assert.Equal(1, clip.Count);

            clip.HoldFolder(12);
            Assert.Equal((ClipKind.Cut, "Folder cut"), (clip.Kind, clip.Says));
            Assert.True(clip.IsCut(12L));
            Assert.False(clip.IsCut(13L));
            Assert.False(clip.IsCut((long?)null));
            Assert.Empty(clip.Cards);

            // A gem in its socket is the character's: gone with it.
            clip.HoldSocketed(one, two);
            Assert.Equal((ClipKind.Cut, 1, "1 item cut"), (clip.Kind, clip.Count, clip.Says));
            Assert.True(clip.IsCutSocketed(two));
            Assert.False(clip.IsCutSocketed(one));
            Assert.Empty(clip.Folders);
            Assert.True(clip.DropItems());
            Assert.True(clip.IsEmpty);

            // Holding nothing is holding nothing.
            clip.HoldItems(ClipKind.Cut, []);
            Assert.True(clip.IsEmpty);
        }
        finally
        {
            clip.Changed -= Changed;
            clip.Clear();
        }
    }
}
